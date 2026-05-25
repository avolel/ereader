namespace EReader.Core.Auth;

public sealed record IssuedRefreshToken(string Token, Guid FamilyId, DateTime ExpiresAt);
