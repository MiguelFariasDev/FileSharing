using FileSharing.Application.DTOs.Auth;
using FluentValidation;

namespace FileSharing.Application.Validators.Auth;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty();

        // Same strength rule as RegisterRequestValidator's Password — a reset must not let a
        // user land on a weaker password than registration would have allowed.
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(100);
    }
}
