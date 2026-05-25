namespace EReader.Core.Interfaces;

public interface ICurrentUserService
{
    Guid GetCurrentUserId();

    bool TryGetCurrentUserId(out Guid userId);

    Guid? GetCurrentFamilyId();
}
