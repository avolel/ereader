using EReader.Core.Auth;

namespace EReader.Api.Dtos;

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessExpiresAt,
    DateTime RefreshExpiresAt,
    UserProfileResponse User)
{
    public static AuthTokenResponse From(AuthTokens tokens) => new(
        tokens.AccessToken,
        tokens.RefreshToken,
        tokens.AccessExpiresAt,
        tokens.RefreshExpiresAt,
        UserProfileResponse.From(tokens.User));
}
