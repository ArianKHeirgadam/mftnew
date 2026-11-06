
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

 // Canonical identity mappings use bigint IDs and normalized UserRoles/Roles.
 // The legacy AppDbContext remains temporarily for modules not yet cut over.
builder.Services.AddDbContext<CanonicalIdentityDbContext>(options =>
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
// --------------------------------------------------
// Canonical database bootstrap
// --------------------------------------------------

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    throw new InvalidOperationException(
        "Automatic EF migrations are disabled for the canonical database. " +
        "Apply reviewed SQL changes explicitly to a restored database copy.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<CanonicalIdentityDbContext>();

    if (builder.Configuration.GetValue<bool>("Bootstrap:CreateSuperAdmin"))
    {
        var username = (builder.Configuration["Bootstrap:Username"] ?? "admin").Trim();
        var existingAdmin = await db.Users.AnyAsync(user => user.Username == username);

        if (!existingAdmin)
        {
            var password = builder.Configuration["Bootstrap:Password"];
            var phoneNumber = builder.Configuration["Bootstrap:Mobile"]?.Trim();

            if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
                throw new InvalidOperationException(
                    "Bootstrap:Password must contain at least 12 characters.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new InvalidOperationException(
                    "Bootstrap:Mobile is required because canonical Users.PhoneNumber is NOT NULL and unique.");

            if (await db.Users.AnyAsync(user => user.PhoneNumber == phoneNumber))
                throw new InvalidOperationException(
                    "Bootstrap:Mobile is already assigned to another user.");

            var role = await db.Roles.SingleOrDefaultAsync(item => item.Id == 1);
            if (role is null || role.Name != CanonicalRoleNames.SuperAdmin)
                throw new InvalidOperationException(
                    "Canonical Roles must contain Id=1, Name=SuperAdmin before bootstrap.");

            var user = new CanonicalUser
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                FullName = builder.Configuration["Bootstrap:FullName"] ?? "System Administrator",
                Email = builder.Configuration["Bootstrap:Email"] ?? "admin@local",
                PhoneNumber = phoneNumber,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            user.UserRoles.Add(new CanonicalUserRole { RoleId = role.Id, Role = role });
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
    }
}

app.Run();
