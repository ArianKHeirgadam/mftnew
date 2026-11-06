using Mft.Application.Contracts;

namespace Mft.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public interface ITokenService
{
    string Create(long userId, string username, string fullName, long? departmentId,
        IReadOnlyCollection<string> roles, DateTime expiresAt);
}
