using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Mft.Application.Abstractions;
using Mft.Application.Contracts;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;

namespace Mft.Infrastructure.Services;

public class TokenService(IConfiguration configuration) : ITokenService
{
    public string Create(User user, DateTime expiresAt)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required.");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("fullName", user.FullName),
            new Claim("departmentId", user.DepartmentId?.ToString() ?? "")
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            configuration["Jwt:Issuer"],
            configuration["Jwt:Audience"],
            claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class AuthService(AppDbContext db, ITokenService tokenService) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(item => item.Department)
            .SingleOrDefaultAsync(
                item => item.Username == request.Username && item.IsActive,
                cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        var expiresAt = DateTime.UtcNow.AddHours(4);
        var accessToken = tokenService.Create(user, expiresAt);
        return new LoginResponse(
            accessToken,
            expiresAt,
            user.Id,
            user.Username,
            user.FullName,
            user.Role.ToString(),
            user.DepartmentId);
    }
}
