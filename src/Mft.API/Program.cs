
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mft.Application.Abstractions;
using Mft.Domain.Entities;
using Mft.Domain;
using Mft.Infrastructure.Persistence;
using Mft.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------
// Services
// --------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();

// --------------------------------------------------
// JWT Authentication
// --------------------------------------------------

var jwtSettings = builder.Configuration.GetSection("Jwt");

var signingKey = jwtSettings["Key"]
    ?? throw new InvalidOperationException(
        "Configure Jwt:Key before starting.");

if (signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must contain at least 32 characters.");
}

builder.Services.AddAuthentication(
    JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtSettings["Issuer"],

                ValidateAudience = true,
                ValidAudience = jwtSettings["Audience"],

                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(signingKey)),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

        // JWTs are otherwise valid until expiry even after an account is disabled
        // or its role/department changes. Re-check the account on every request
        // so stale claims cannot keep access to the previous permission scope.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?
                    .FindFirstValue(ClaimTypes.NameIdentifier);

                if (!Guid.TryParse(userIdValue, out var userId))
                {
                    context.Fail("توکن فاقد شناسه کاربر معتبر است.");
                    return;
                }

                var db = context.HttpContext.RequestServices
                    .GetRequiredService<AppDbContext>();

                var account = await db.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => new
                    {
                        user.IsActive,
                        Role = user.Role.ToString(),
                        user.DepartmentId
                    })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                if (account is null || !account.IsActive)
                {
                    context.Fail("حساب کاربری غیرفعال یا حذف شده است.");
                    return;
                }

                var tokenRole = context.Principal?
                    .FindFirstValue(ClaimTypes.Role);
                var tokenDepartment = context.Principal?
                    .FindFirstValue("departmentId") ?? "";

                var currentDepartment = account.DepartmentId?.ToString() ?? "";
                if (!string.Equals(tokenRole, account.Role, StringComparison.Ordinal) ||
                    !string.Equals(tokenDepartment, currentDepartment, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("سطح دسترسی حساب تغییر کرده است؛ دوباره وارد شوید.");
                }
            }
        };
    });

// --------------------------------------------------
// Authorization Policies
// --------------------------------------------------

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin",
        policy => policy.RequireRole("SuperAdmin"));

    options.AddPolicy("DepartmentOrAbove",
        policy => policy.RequireRole(
            "SuperAdmin", "Department"));

    options.AddPolicy("TeacherOrAbove",
        policy => policy.RequireRole(
            "SuperAdmin", "Department", "Teacher"));
});

// --------------------------------------------------
// CORS
// --------------------------------------------------

var frontendOrigins =
    builder.Configuration
        .GetSection("Frontend:Origins")
        .Get<string[]>()
    ?? new[]
    {
        "http://localhost:5500",
        "http://127.0.0.1:5500",
        "http://localhost:5173"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// --------------------------------------------------
// Build Application
// --------------------------------------------------

var app = builder.Build();

app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Mft.API"
}));

// --------------------------------------------------
// Database & SuperAdmin Bootstrap
// --------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    // Apply migrations if enabled
    if (builder.Configuration.GetValue<bool>(
        "Database:ApplyMigrationsOnStartup"))
    {
        await db.Database.MigrateAsync();
    }

    // Create first SuperAdmin
    if (builder.Configuration.GetValue<bool>(
        "Bootstrap:CreateSuperAdmin"))
    {
        var username =
            builder.Configuration["Bootstrap:Username"]
            ?? "admin";

        var existingAdmin = await db.Users
            .AnyAsync(user => user.Username == username);

        if (!existingAdmin)
        {
            var password =
                builder.Configuration["Bootstrap:Password"];

            if (string.IsNullOrWhiteSpace(password) ||
                password.Length < 12)
            {
                throw new InvalidOperationException(
                    "Bootstrap:Password must be at least 12 characters.");
            }

            db.Users.Add(new User
            {
                Username = username,
                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(password),

                FullName =
                    builder.Configuration["Bootstrap:FullName"]
                    ?? "System Administrator",

                Email =
                    builder.Configuration["Bootstrap:Email"]
                    ?? "admin@local",

                Mobile =
                    builder.Configuration["Bootstrap:Mobile"]
                    ?? "",

                Role = UserRole.SuperAdmin,
                MustChangePassword = false,
                IsActive = true
            });

            await db.SaveChangesAsync();
        }
    }

    // Create second SuperAdmin
    if (builder.Configuration.GetValue<bool>(
        "Bootstrap:SecondSuperAdmin:Enabled"))
    {
        var secondUsername =
            builder.Configuration[
                "Bootstrap:SecondSuperAdmin:Username"]
            ?? "admin2";

        var existingSecondAdmin = await db.Users
            .AnyAsync(user =>
                user.Username == secondUsername);

        if (!existingSecondAdmin)
        {
            var secondPassword =
                builder.Configuration[
                    "Bootstrap:SecondSuperAdmin:Password"];

            if (string.IsNullOrWhiteSpace(secondPassword) ||
                secondPassword.Length < 12)
            {
                throw new InvalidOperationException(
                    "Second SuperAdmin password must be at least 12 characters.");
            }

            db.Users.Add(new User
            {
                Username = secondUsername,

                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        secondPassword),

                FullName = "Second System Administrator",
                Email = "admin2@local",
                Mobile = "",

                Role = UserRole.SuperAdmin,
                MustChangePassword = false,
                IsActive = true
            });

            await db.SaveChangesAsync();
        }
    }
}

app.Run();
