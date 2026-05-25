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
        var userId = _currentUser.GetCurrentUserId();

        if (request.Username is null)
        {
            // No-op update: PATCH with nothing to change. Return the current state.
            var current = await _users.GetCurrentAsync(userId, ct);
            return Ok(UserProfileResponse.From(current));
        }

        var updated = await _users.UpdateUsernameAsync(userId, request.Username, ct);
        return Ok(UserProfileResponse.From(updated));
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword))
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
