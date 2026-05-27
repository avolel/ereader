using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IUserService
{
    Task<User> GetCurrentAsync(Guid userId, CancellationToken ct);

    Task<User> UpdateUsernameAsync(Guid userId, string newUsername, CancellationToken ct);

    Task ChangePasswordAsync(
        Guid userId,
        Guid? currentFamilyId,
        string currentPassword,
        string newPassword,
        bool revokeOtherSessions,
        CancellationToken ct);
}
