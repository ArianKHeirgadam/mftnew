using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mft.Application.Abstractions;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;
using Mft.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();

var jwtSettings = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSettings["Key"]
    ?? throw new InvalidOperationException("Configure Jwt:Key (32+ chars) before starting.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdmin", policy => policy.RequireRole("SuperAdmin"));
    options.AddPolicy("DepartmentOrAbove", policy => policy.RequireRole("SuperAdmin", "Department"));
    options.AddPolicy("TeacherOrAbove", policy => policy.RequireRole("SuperAdmin", "Department", "Teacher"));
});

var frontendOrigins = builder.Configuration.GetSection("Frontend:Origins").Get<string[]>()
    ?? ["http://localhost:5500", "http://127.0.0.1:5500", "http://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(frontendOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
// A temporary-password account may access only identity and password-change endpoints.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isPasswordLifecycleEndpoint = path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/auth/me", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase);
    if (context.User.Identity?.IsAuthenticated == true && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !isPasswordLifecycleEndpoint)
    {
        var idValue = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(idValue, out var userId))
        {
            var account = await context.RequestServices.GetRequiredService<AppDbContext>()
                .Users.AsNoTracking().Where(user => user.Id == userId)
                .Select(user => new { user.MustChangePassword, user.IsActive }).SingleOrDefaultAsync(context.RequestAborted);
            if (account is null || !account.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (account.MustChangePassword)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new { message = "برای ادامه ابتدا باید رمز موقت خود را تغییر دهید.", code = "PASSWORD_CHANGE_REQUIRED" }, context.RequestAborted);
                return;
            }
        }
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Mft.API" }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
    {
        await db.Database.MigrateAsync();
    }

    if (builder.Configuration.GetValue<bool>("Bootstrap:CreateSuperAdmin"))
    {
        var username = builder.Configuration["Bootstrap:Username"] ?? "admin";
        if (!await db.Users.AnyAsync(user => user.Username == username))
        {
            var password = builder.Configuration["Bootstrap:Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Set Bootstrap:Password (use dotnet user-secrets).");
            }

            db.Users.Add(new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                FullName = builder.Configuration["Bootstrap:FullName"] ?? "System Administrator",
                Email = builder.Configuration["Bootstrap:Email"] ?? "admin@local",
                Mobile = builder.Configuration["Bootstrap:Mobile"] ?? "",
                Role = UserRole.SuperAdmin,
                MustChangePassword = false,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }
}

app.Run();