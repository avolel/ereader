using EReader.Api.Dtos;
using EReader.Core.Exceptions;
using EReader.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EReader.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService auth, ICurrentUserService currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthTokenResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var tokens = await _auth.RegisterAsync(request.Username, request.Password, ct);
        var response = AuthTokenResponse.From(tokens);
        return CreatedAtAction(
            actionName: "Me",
            controllerName: "Users",
            routeValues: null,
            value: response);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var tokens = await _auth.LoginAsync(request.Username, request.Password, ct);
        return Ok(AuthTokenResponse.From(tokens));
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthTokenResponse>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var tokens = await _auth.RefreshAsync(request.RefreshToken, ct);
        return Ok(AuthTokenResponse.From(tokens));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new ValidationException("Refresh token is required.");
        }

        await _auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var userId = _currentUser.GetCurrentUserId();
        await _auth.LogoutAllAsync(userId, ct);
        return NoContent();
    }
}
