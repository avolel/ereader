namespace EReader.Data.Auth;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "ereader:";

    // Lifetime of refresh tokens persisted by RedisRefreshTokenStore. Lives here
    // (not on JwtOptions) because refresh tokens aren't JWTs in this design —
    // they're opaque CSPRNG strings backed by Redis.
    public int RefreshTokenDays { get; set; } = 30;
}
