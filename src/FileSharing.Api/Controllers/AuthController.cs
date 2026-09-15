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
    private readonly IPasswordResetService _passwordResetService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ForgotPasswordRequest> _forgotPasswordValidator;
    private readonly IValidator<ResetPasswordRequest> _resetPasswordValidator;

    public AuthController(
        IAuthService authService,
        IPasswordResetService passwordResetService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<ForgotPasswordRequest> forgotPasswordValidator,
        IValidator<ResetPasswordRequest> resetPasswordValidator)
    {
        _authService = authService;
        _passwordResetService = passwordResetService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _forgotPasswordValidator = forgotPasswordValidator;
        _resetPasswordValidator = resetPasswordValidator;
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

        var user = await _authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, user);
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

        var auth = await _authService.LoginAsync(request, cancellationToken);
        return Ok(auth);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(user);
    }

    /// <summary>
    /// Always answers 202 with the same body, whether or not the email belongs to an account —
    /// see PasswordResetService.ForgotPasswordAsync and Auth/PasswordResetEnumerationTests. Its
    /// own, tighter rate-limit policy overrides the class-level Auth one for this one action —
    /// see RateLimiterPolicyNames.PasswordReset.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimiterPolicyNames.PasswordReset)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var validation = await _forgotPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState);
            return ValidationProblem(ModelState);
        }

        await _passwordResetService.ForgotPasswordAsync(request.Email, cancellationToken);

        return Accepted(new { message = "Se a conta existir, enviaremos instruções para redefinir sua senha." });
    }

    /// <summary>
    /// Lets a client (the Web reset-password page, on load) check a token before showing the
    /// "new password" form — no user information is ever included in the response, only
    /// "valid" (200) or the specific reason it is not (see PasswordResetService).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("reset-password/{token}")]
    public async Task<IActionResult> ValidateResetToken(string token, CancellationToken cancellationToken)
    {
        await _passwordResetService.ValidateResetTokenAsync(token, cancellationToken);
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var validation = await _resetPasswordValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState);
            return ValidationProblem(ModelState);
        }

        await _passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return Ok();
    }
}
