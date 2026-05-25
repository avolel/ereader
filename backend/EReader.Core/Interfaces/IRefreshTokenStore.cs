using EReader.Core.Auth;

namespace EReader.Core.Interfaces;

public interface IRefreshTokenStore
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? familyId, CancellationToken ct);

    Task<ConsumedRefreshToken> ValidateAndConsumeAsync(string rawToken, CancellationToken ct);

    Task RevokeAsync(string rawToken, CancellationToken ct);

    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct);

    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);

    Task RevokeOtherFamiliesAsync(Guid userId, Guid keepFamilyId, CancellationToken ct);
}
