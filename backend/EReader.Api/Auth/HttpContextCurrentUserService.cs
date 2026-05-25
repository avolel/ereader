using System.Security.Claims;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using EReader.Data.Auth;

namespace EReader.Api.Auth;

public sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid GetCurrentUserId()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            throw new AuthenticationException("NO_USER", "No authenticated user on the current request.");
        }
        return userId;
    }

    public bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var user = _accessor.HttpContext?.User;
        if (user is null) return false;

        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out userId);
    }

    public Guid? GetCurrentFamilyId()
    {
        var raw = _accessor.HttpContext?.User?.FindFirst(JwtTokenIssuer.FamilyClaim)?.Value;
        return Guid.TryParse(raw, out var familyId) ? familyId : null;
    }
}
