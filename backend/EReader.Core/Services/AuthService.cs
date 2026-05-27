using EReader.Core.Auth;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;

namespace EReader.Core.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenIssuer _jwt;
    private readonly IRefreshTokenStore _refreshTokens;

    public AuthService(
        IUserRepository users,
        IPasswordHasher hasher,
        IJwtTokenIssuer jwt,
        IRefreshTokenStore refreshTokens)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
    }

    public async Task<AuthTokens> RegisterAsync(string username, string password, CancellationToken ct)
    {
        CredentialValidator.ValidateUsername(username);
        CredentialValidator.ValidatePassword(password);

        if (await _users.UsernameExistsAsync(username, ct))
        {
            throw new ConflictException("Username is already taken.", "USERNAME_TAKEN");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = _hasher.Hash(password),
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
        };

        await _users.AddAsync(user, ct);
        return await IssueNewSessionAsync(user, ct);
    }

    public async Task<AuthTokens> LoginAsync(string username, string password, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(username, ct);

        // Invariant: same opaque failure for "no such user" and "wrong password"
        // so the API doesn't leak which usernames exist.
        if (user is null || !_hasher.Verify(password, user.PasswordHash))
        {
            throw new AuthenticationException("INVALID_CREDENTIALS", "Username or password is incorrect.");
        }

        if (!user.IsActive)
        {
            throw new AuthenticationException("INVALID_CREDENTIALS", "Username or password is incorrect.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user, ct);

        return await IssueNewSessionAsync(user, ct);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthenticationException("REFRESH_INVALID", "Refresh token is required.");
        }

        var consumed = await _refreshTokens.ValidateAndConsumeAsync(refreshToken, ct);

        var user = await _users.GetByIdAsync(consumed.UserId, ct)
            ?? throw new AuthenticationException("REFRESH_INVALID", "Refresh token is invalid.");

        if (!user.IsActive)
        {
            // Disabled user shouldn't be able to rotate their way back in. Kill
            // the family so the next attempt is even cleaner.
            await _refreshTokens.RevokeFamilyAsync(consumed.FamilyId, ct);
            throw new AuthenticationException("INVALID_CREDENTIALS", "User is inactive.");
        }

        return await IssueRotatedSessionAsync(user, consumed.FamilyId, ct);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return Task.CompletedTask;
        return _refreshTokens.RevokeAsync(refreshToken, ct);
    }

    public Task LogoutAllAsync(Guid userId, CancellationToken ct) =>
        _refreshTokens.RevokeAllForUserAsync(userId, ct);

    private async Task<AuthTokens> IssueNewSessionAsync(User user, CancellationToken ct)
    {
        var refresh = await _refreshTokens.IssueAsync(user.Id, familyId: null, ct);
        var access = _jwt.IssueAccessToken(user, refresh.FamilyId);
        return new AuthTokens(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            refresh.ExpiresAt,
            user);
    }

    private async Task<AuthTokens> IssueRotatedSessionAsync(User user, Guid familyId, CancellationToken ct)
    {
        var refresh = await _refreshTokens.IssueAsync(user.Id, familyId, ct);
        var access = _jwt.IssueAccessToken(user, refresh.FamilyId);
        return new AuthTokens(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            refresh.ExpiresAt,
            user);
    }
}
