using EReader.Core.Auth;

namespace EReader.Core.Interfaces;

public interface IAuthService
{
    Task<AuthTokens> RegisterAsync(string username, string password, CancellationToken ct);

    Task<AuthTokens> LoginAsync(string username, string password, CancellationToken ct);

    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct);

    Task LogoutAsync(string refreshToken, CancellationToken ct);

    Task LogoutAllAsync(Guid userId, CancellationToken ct);
}
