using EReader.Core.Interfaces;

namespace EReader.Data.Auth;

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    // 12 rounds: ~250ms on modern hardware. Tune up if hashing becomes a perf
    // win for the attacker; tune down if login latency dominates user feedback.
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
