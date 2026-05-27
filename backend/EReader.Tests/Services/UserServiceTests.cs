using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokens = new();

    private UserService BuildService() =>
        new(_users.Object, _hasher.Object, _refreshTokens.Object);

    private static User BuildUser() => new()
    {
        Id = Guid.NewGuid(),
        Username = "alice",
        PasswordHash = "stored-hash",
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        IsActive = true,
    };

    [Fact]
    public async Task Should_ThrowConflict_When_NewUsernameTaken_OnPatch()
    {
        var user = BuildUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users
            .Setup(r => r.UsernameExistsAsync("bob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService();

        var act = async () => await service.UpdateUsernameAsync(user.Id, "bob", CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "USERNAME_TAKEN");
        _users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_VerifyOldPassword_When_ChangingPassword()
    {
        var user = BuildUser();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("wrong-old", user.PasswordHash)).Returns(false);

        var service = BuildService();

        var act = async () => await service.ChangePasswordAsync(
            user.Id,
            currentFamilyId: Guid.NewGuid(),
            currentPassword: "wrong-old",
            newPassword: "NewPassword1",
            revokeOtherSessions: false,
            CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.Code == "INVALID_CREDENTIALS");
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RevokeOtherSessions_When_ChangePasswordWithFlag()
    {
        var user = BuildUser();
        var currentFamily = Guid.NewGuid();
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("OldPassword1", user.PasswordHash)).Returns(true);
        _hasher.Setup(h => h.Hash("NewPassword1")).Returns("new-hash");

        var service = BuildService();

        await service.ChangePasswordAsync(
            user.Id,
            currentFamilyId: currentFamily,
            currentPassword: "OldPassword1",
            newPassword: "NewPassword1",
            revokeOtherSessions: true,
            CancellationToken.None);

        _refreshTokens.Verify(
            s => s.RevokeOtherFamiliesAsync(user.Id, currentFamily, It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokens.Verify(
            s => s.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
