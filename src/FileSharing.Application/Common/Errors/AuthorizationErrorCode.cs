namespace FileSharing.Application.Common.Errors;

/// <summary>
/// "You are authenticated but not allowed to do this" (403) — deliberately separate from
/// <see cref="AuthErrorCode"/> (401: "you are not who you claim to be, or not authenticated at
/// all") so the two HTTP semantics are never conflated.
/// </summary>
public enum AuthorizationErrorCode
{
    Forbidden
}
