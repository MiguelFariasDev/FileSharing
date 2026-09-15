using FileSharing.Application.Common.Errors;

namespace FileSharing.Application.Common.Exceptions;

/// <summary>
/// "You are not who you claim to be, or not authenticated at all" — 401 by default. Never used
/// for "you're authenticated but not allowed" (see ForbiddenException) — keeping the two
/// separate is what stops 401/403 from ever being conflated (see AuthorizationErrorCode remarks).
/// </summary>
public sealed class AuthenticationException : AppException
{
    // 401 — a plain int, never Microsoft.AspNetCore.Http.StatusCodes: Application must not
    // reference ASP.NET Core types (see CLAUDE.md's layering rules).
    public AuthenticationException(AuthErrorCode code, string message, int httpStatusCode = 401)
        : base(httpStatusCode, code, ErrorCodeCatalog.Map(code), message)
    {
    }
}
