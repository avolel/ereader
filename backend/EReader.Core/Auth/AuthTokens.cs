using EReader.Core.Models;

namespace EReader.Core.Auth;

public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessExpiresAt,
    string RefreshToken,
    DateTime RefreshExpiresAt,
    User User);
