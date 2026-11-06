using Mft.Application.Contracts;
using Mft.Infrastructure.Persistence;

namespace Mft.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public interface ITokenService
{
    string Create(CanonicalUser user, IReadOnlyCollection<string> roles, DateTime expiresAt);
}
