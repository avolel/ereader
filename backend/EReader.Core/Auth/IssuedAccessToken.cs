namespace EReader.Core.Auth;

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt);
