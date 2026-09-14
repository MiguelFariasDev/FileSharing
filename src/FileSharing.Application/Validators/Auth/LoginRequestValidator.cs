using FileSharing.Application.DTOs.Auth;
using FluentValidation;

namespace FileSharing.Application.Validators.Auth;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            // Same cap as RegisterRequestValidator — without it, an unauthenticated caller could
            // submit an arbitrarily large password on every login attempt, forcing the server to
            // run ASP.NET Core Identity's PBKDF2 hasher over it each time (cost scales with input
            // size), a cheap resource-exhaustion lever this endpoint has no reason to allow.
            .MaximumLength(100);
    }
}
