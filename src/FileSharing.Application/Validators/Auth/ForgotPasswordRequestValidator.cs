using FileSharing.Application.DTOs.Auth;
using FluentValidation;

namespace FileSharing.Application.Validators.Auth;

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);
    }
}
