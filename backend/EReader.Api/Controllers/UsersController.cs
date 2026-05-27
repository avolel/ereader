using EReader.Api.Dtos;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _users;
    private readonly ICurrentUserService _currentUser;

    public UsersController(IUserService users, ICurrentUserService currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    [HttpGet("me", Name = "Me")]
    public async Task<ActionResult<UserProfileResponse>> Me(CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        var user = await _users.GetCurrentAsync(userId, ct);
        return Ok(UserProfileResponse.From(user));
    }

    [HttpPatch("me")]
    public async Task<ActionResult<UserProfileResponse>> UpdateMe(
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        if (request.Username is null)
        {
            // PATCH with nothing to change: be honest about it (and skip the DB
            // roundtrip that re-reads the unchanged user).
            return NoContent();
        }

        var userId = _currentUser.GetCurrentUserId();
        var updated = await _users.UpdateUsernameAsync(userId, request.Username, ct);
        return Ok(UserProfileResponse.From(updated));
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            throw new ValidationException("Current password is required.");
        }

        var userId = _currentUser.GetCurrentUserId();
        var familyId = _currentUser.GetCurrentFamilyId();

        await _users.ChangePasswordAsync(
            userId,
            familyId,
            request.CurrentPassword,
            request.NewPassword,
            request.RevokeOtherSessions,
            ct);

        return NoContent();
    }
}
