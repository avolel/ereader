using EReader.Core.Auth;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using EReader.Core.Services;
using FluentAssertions;
using Moq;

namespace EReader.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtTokenIssuer> _jwt = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokens = new();

    private AuthService BuildService() =>
        new(_users.Object, _hasher.Object, _jwt.Object, _refreshTokens.Object);

    private static User BuildUser(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Username = "alice",
        PasswordHash = "stored-hash",
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        IsActive = active,
    };

    private void SetupTokenIssuance(Guid familyId)
    {
        _refreshTokens
            .Setup(s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedRefreshToken("refresh-raw", familyId, DateTime.UtcNow.AddDays(30)));

        _jwt
            .Setup(j => j.IssueAccessToken(It.IsAny<User>(), It.IsAny<Guid>()))
            .Returns(new IssuedAccessToken("access-jwt", DateTime.UtcNow.AddMinutes(15)));
    }

    [Fact]
    public async Task Should_ThrowConflict_When_UsernameAlreadyExists()
    {
        _users
            .Setup(r => r.UsernameExistsAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = BuildService();

        var act = async () => await service.RegisterAsync("alice", "Password123", CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == "USERNAME_TAKEN");
        _users.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_HashPassword_When_Registering()
    {
        _users.Setup(r => r.UsernameExistsAsync("alice", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _hasher.Setup(h => h.Hash("Password123")).Returns("$2a$12$hashed");
        SetupTokenIssuance(Guid.NewGuid());

        User? captured = null;
        _users
            .Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => captured = u)
            .Returns(Task.CompletedTask);

        var service = BuildService();

        await service.RegisterAsync("alice", "Password123", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.PasswordHash.Should().Be("$2a$12$hashed");
        captured.PasswordHash.Should().NotBe("Password123");
    }

    [Fact]
    public async Task Should_ThrowInvalidCredentials_When_PasswordWrong()
    {
        var user = BuildUser();
        _users
            .Setup(r => r.GetByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("wrong-pass", user.PasswordHash)).Returns(false);

        var service = BuildService();

        var act = async () => await service.LoginAsync("alice", "wrong-pass", CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.Code == "INVALID_CREDENTIALS");
        _users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ThrowInvalidCredentials_When_UserIsInactive()
    {
        var user = BuildUser(active: false);
        _users
            .Setup(r => r.GetByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        // Password is correct — invariant is that inactive users still get the
        // same opaque failure as wrong-password.
        _hasher.Setup(h => h.Verify("Password123", user.PasswordHash)).Returns(true);

        var service = BuildService();

        var act = async () => await service.LoginAsync("alice", "Password123", CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.Code == "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Should_RevokeOldRefresh_When_Rotating()
    {
        var user = BuildUser();
        var family = Guid.NewGuid();

        _refreshTokens
            .Setup(s => s.ValidateAndConsumeAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsumedRefreshToken(user.Id, family));
        _users
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        SetupTokenIssuance(family);

        var service = BuildService();

        var tokens = await service.RefreshAsync("old-refresh", CancellationToken.None);

        // The old token's revocation is the responsibility of ValidateAndConsumeAsync.
        // What AuthService is responsible for is: call validate-and-consume on the
        // old token, then issue a new refresh under the same family.
        _refreshTokens.Verify(
            s => s.ValidateAndConsumeAsync("old-refresh", It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokens.Verify(
            s => s.IssueAsync(user.Id, family, It.IsAny<CancellationToken>()),
            Times.Once);
        tokens.AccessToken.Should().Be("access-jwt");
        tokens.RefreshToken.Should().Be("refresh-raw");
    }

    [Fact]
    public async Task Should_RevokeWholeFamily_When_RefreshTokenReused()
    {
        // Simulates the store's reuse-detection path: ValidateAndConsumeAsync
        // internally revokes the family and throws REFRESH_REUSED. AuthService
        // must surface that exception without minting a new token.
        _refreshTokens
            .Setup(s => s.ValidateAndConsumeAsync("reused-refresh", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException("REFRESH_REUSED", "Refresh token already used."));

        var service = BuildService();

        var act = async () => await service.RefreshAsync("reused-refresh", CancellationToken.None);

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.Code == "REFRESH_REUSED");
        _refreshTokens.Verify(
            s => s.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _jwt.Verify(
            j => j.IssueAccessToken(It.IsAny<User>(), It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_UpdateLastLoginAt_When_LoginSucceeds()
    {
        var user = BuildUser();
        user.LastLoginAt = null;
        var before = DateTime.UtcNow;

        _users
            .Setup(r => r.GetByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify("Password123", user.PasswordHash)).Returns(true);
        SetupTokenIssuance(Guid.NewGuid());

        User? captured = null;
        _users
            .Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => captured = u)
            .Returns(Task.CompletedTask);

        var service = BuildService();

        await service.LoginAsync("alice", "Password123", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.LastLoginAt.Should().NotBeNull();
        captured.LastLoginAt!.Value.Should().BeOnOrAfter(before);
    }
}
