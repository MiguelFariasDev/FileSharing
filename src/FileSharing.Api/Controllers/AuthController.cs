using FileSharing.Api.Extensions;
using FileSharing.Application.DTOs.Auth;
using FileSharing.Application.Services.Auth;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FileSharing.Api.Controllers;

/// <summary>
/// Register/Login are the only unauthenticated actions here and the obvious target for
/// credential-stuffing/brute-force — [EnableRateLimiting] slows that down per client IP (see
/// RateLimiterPolicyNames.Auth in Program.cs). Applied at the class level rather than per-action
/// since Me carries [Authorize] already and is not a meaningful brute-force target either way.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimiterPolicyNames.Auth)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;

    public AuthController(
        IAuthService authService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator)
    {
        _authService = authService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var validation = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState);
            return ValidationProblem(ModelState);
        }

        var result = await _authService.RegisterAsync(request, cancellationToken);
        if (!result.IsSuccess)
            return Conflict(new { message = result.Error });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var validation = await _loginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState);
            return ValidationProblem(ModelState);
        }

        var result = await _authService.LoginAsync(request, cancellationToken);
        if (!result.IsSuccess)
            return Unauthorized(new { message = result.Error });

        return Ok(result.Value);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        if (!result.IsSuccess)
            return Unauthorized();

        return Ok(result.Value);
    }
}
