using Mft.Application.Contracts;
using Mft.Domain.Entities;

namespace Mft.Application.Abstractions;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}

public interface ITokenService
{
    string Create(User user, DateTime expiresAt);
}
