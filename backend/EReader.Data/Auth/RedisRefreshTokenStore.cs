using System.Security.Cryptography;
using System.Text;
using EReader.Core.Auth;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EReader.Data.Auth;

/// <summary>
/// Refresh tokens are 32 bytes of CSPRNG output, base64url-encoded. We store
/// SHA-256(token) so a Redis dump never gives back a usable credential. The
/// token row holds (userId, familyId, revoked); families and per-user families
/// are tracked in sets so we can revoke by family or by user.
/// </summary>
public sealed class RedisRefreshTokenStore : IRefreshTokenStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly JwtOptions _jwt;
    private readonly RedisOptions _redisOptions;

    // Atomic check-and-revoke: returns 0=not_found, 1=consumed_ok, 2=reuse_detected.
    // KEYS[1] = token hash key.
    private const string ConsumeScript = @"
        local row = redis.call('HMGET', KEYS[1], 'userId', 'familyId', 'revoked')
        if (not row[1]) then return {0} end
        if (row[3] == '1') then return {2, row[1], row[2]} end
        redis.call('HSET', KEYS[1], 'revoked', '1')
        return {1, row[1], row[2]}
    ";

    public RedisRefreshTokenStore(
        IConnectionMultiplexer redis,
        IOptions<JwtOptions> jwt,
        IOptions<RedisOptions> redisOptions)
    {
        _redis = redis;
        _jwt = jwt.Value;
        _redisOptions = redisOptions.Value;
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? familyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var raw = GenerateToken();
        var hash = Hash(raw);
        var family = familyId ?? Guid.NewGuid();
        var ttl = TimeSpan.FromDays(_jwt.RefreshTokenDays);
        var expiresAt = DateTime.UtcNow.Add(ttl);

        var db = _redis.GetDatabase();
        var tx = db.CreateTransaction();

        // We intentionally don't await these inside the transaction — they
        // queue and execute atomically when ExecuteAsync is called.
        _ = tx.HashSetAsync(TokenKey(hash), new HashEntry[]
        {
            new("userId", userId.ToString()),
            new("familyId", family.ToString()),
            new("expiresAt", expiresAt.ToString("O")),
            new("revoked", "0"),
        });
        _ = tx.KeyExpireAsync(TokenKey(hash), ttl);
        _ = tx.SetAddAsync(FamilyKey(family), hash);
        _ = tx.KeyExpireAsync(FamilyKey(family), ttl);
        _ = tx.SetAddAsync(UserKey(userId), family.ToString());
        _ = tx.KeyExpireAsync(UserKey(userId), ttl);

        if (!await tx.ExecuteAsync())
        {
            throw new InvalidOperationException("Failed to persist refresh token to Redis.");
        }

        return new IssuedRefreshToken(raw, family, expiresAt);
    }

    public async Task<ConsumedRefreshToken> ValidateAndConsumeAsync(string rawToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var hash = Hash(rawToken);
        var db = _redis.GetDatabase();

        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            ConsumeScript,
            new RedisKey[] { TokenKey(hash) });

        if (result is null || result.Length == 0)
        {
            throw new AuthenticationException("REFRESH_INVALID", "Refresh token is invalid or expired.");
        }

        var status = (int)result[0];
        switch (status)
        {
            case 0:
                throw new AuthenticationException("REFRESH_INVALID", "Refresh token is invalid or expired.");

            case 1:
                var userId = Guid.Parse((string)result[1]!);
                var familyId = Guid.Parse((string)result[2]!);
                return new ConsumedRefreshToken(userId, familyId);

            case 2:
                // Reuse of an already-consumed token: treat as theft, kill the whole family.
                var reuseFamilyId = Guid.Parse((string)result[2]!);
                await RevokeFamilyAsync(reuseFamilyId, ct);
                throw new AuthenticationException(
                    "REFRESH_REUSED",
                    "Refresh token has already been used. All sessions in this family have been revoked.");

            default:
                throw new AuthenticationException("REFRESH_INVALID", "Unexpected refresh token state.");
        }
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var hash = Hash(rawToken);
        var db = _redis.GetDatabase();

        // Keep the row until natural TTL so reuse detection still trips if a
        // copy of this token shows up later. Just flip revoked.
        await db.HashSetAsync(TokenKey(hash), "revoked", "1");
    }

    public async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var hashes = await db.SetMembersAsync(FamilyKey(familyId));
        if (hashes.Length == 0) return;

        var tx = db.CreateTransaction();
        foreach (var h in hashes)
        {
            _ = tx.HashSetAsync(TokenKey(h.ToString()), "revoked", "1");
        }

        await tx.ExecuteAsync();
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var families = await db.SetMembersAsync(UserKey(userId));
        foreach (var fam in families)
        {
            if (Guid.TryParse(fam.ToString(), out var familyId))
            {
                await RevokeFamilyAsync(familyId, ct);
            }
        }
    }

    public async Task RevokeOtherFamiliesAsync(Guid userId, Guid keepFamilyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var db = _redis.GetDatabase();
        var families = await db.SetMembersAsync(UserKey(userId));
        foreach (var fam in families)
        {
            if (Guid.TryParse(fam.ToString(), out var familyId) && familyId != keepFamilyId)
            {
                await RevokeFamilyAsync(familyId, ct);
            }
        }
    }

    private RedisKey TokenKey(string hash) =>
        $"{_redisOptions.InstanceName}refresh:token:{hash}";

    private RedisKey FamilyKey(Guid familyId) =>
        $"{_redisOptions.InstanceName}refresh:family:{familyId}";

    private RedisKey UserKey(Guid userId) =>
        $"{_redisOptions.InstanceName}refresh:user:{userId}";

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Hash(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
