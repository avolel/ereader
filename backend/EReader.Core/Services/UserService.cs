using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;

namespace EReader.Core.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IRefreshTokenStore _refreshTokens;

    public UserService(
        IUserRepository users,
        IPasswordHasher hasher,
        IRefreshTokenStore refreshTokens)
    {
        _users = users;
        _hasher = hasher;
        _refreshTokens = refreshTokens;
    }

    public async Task<User> GetCurrentAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new AuthenticationException("NO_USER", "User no longer exists.");
        return user;
    }

    public async Task<User> UpdateUsernameAsync(Guid userId, string newUsername, CancellationToken ct)
    {
        CredentialValidator.ValidateUsername(newUsername);

        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new AuthenticationException("NO_USER", "User no longer exists.");

        if (string.Equals(user.Username, newUsername, StringComparison.OrdinalIgnoreCase))
        {
            return user;
        }

        if (await _users.UsernameExistsAsync(newUsername, ct))
        {
            throw new ConflictException("Username is already taken.", "USERNAME_TAKEN");
        }

        user.Username = newUsername;
        await _users.UpdateAsync(user, ct);
        return user;
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        Guid? currentFamilyId,
        string currentPassword,
        string newPassword,
        bool revokeOtherSessions,
        CancellationToken ct)
    {
        CredentialValidator.ValidatePassword(newPassword);

        var user = await _users.GetByIdAsync(userId, ct)
            ?? throw new AuthenticationException("NO_USER", "User no longer exists.");

        if (!_hasher.Verify(currentPassword, user.PasswordHash))
        {
            throw new AuthenticationException("INVALID_CREDENTIALS", "Current password is incorrect.");
        }

        user.PasswordHash = _hasher.Hash(newPassword);
        await _users.UpdateAsync(user, ct);

        if (!revokeOtherSessions) return;

        if (currentFamilyId is { } keep)
        {
            await _refreshTokens.RevokeOtherFamiliesAsync(userId, keep, ct);
        }
        else
        {
            // No family on the current token (shouldn't happen in normal flow,
            // since IssueAccessToken always stamps it). Fall back to revoking
            // everything to avoid leaving stale sessions alive.
            await _refreshTokens.RevokeAllForUserAsync(userId, ct);
        }
    }
}
