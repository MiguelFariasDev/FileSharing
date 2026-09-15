namespace FileSharing.Application.Common.Errors;

/// <summary>
/// Field-level details never live in this enum — they keep flowing through FluentValidation's
/// existing ModelState/ValidationProblemDetails "errors" dictionary (see
/// Program.cs' CustomizeProblemDetails). This single value only exists so a validation failure's
/// top-level "code" is as stable and typed as every other error family.
/// </summary>
public enum ValidationErrorCode
{
    InvalidRequest
}
