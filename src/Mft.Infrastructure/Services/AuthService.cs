using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Mft.Application.Abstractions;
using Mft.Application.Contracts;
using Mft.Infrastructure.Persistence;

namespace Mft.Infrastructure.Services;

public sealed class TokenService(IConfiguration configuration) : ITokenService
{
    public string Create(long userId, string username, string fullName, long? departmentId,
        IReadOnlyCollection<string> roles, DateTime expiresAt)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new("fullName", fullName)
        };

        if (departmentId.HasValue)
            claims.Add(new Claim("departmentId", departmentId.Value.ToString()));

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
            claims.Add(new Claim(ClaimTypes.Role, CanonicalRoleNames.ToPolicyRole(role)));

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

public sealed class AuthService(
    CanonicalIdentityDbContext db,
    ITokenService tokenService) : IAuthService
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var user = await db.Users
            .AsNoTracking()
            .Include(item => item.Department)
            .Include(item => item.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(
                item => item.Username == username && item.IsActive,
                cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        var databaseRoles = user.UserRoles
            .Select(item => item.Role.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (databaseRoles.Length == 0)
            return null;

        var policyRoles = databaseRoles
            .Select(CanonicalRoleNames.ToPolicyRole)
            .ToArray();

        var primaryRole = new[] { "SuperAdmin", "Department", "Teacher", "Student" }
            .FirstOrDefault(role => policyRoles.Contains(role, StringComparer.Ordinal));

        if (primaryRole is null)
            return null;

        var expiresAt = DateTime.UtcNow.AddHours(4);
        var accessToken = tokenService.Create(
            user.Id, user.Username, user.FullName, user.DepartmentId,
            databaseRoles, expiresAt);

        return new LoginResponse(
            accessToken,
            expiresAt,
            user.Id,
            user.Username,
            user.FullName,
            primaryRole,
            user.DepartmentId);
    }
}
