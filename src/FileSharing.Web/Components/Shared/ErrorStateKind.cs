namespace FileSharing.Web.Components.Shared;

/// <summary>
/// The reusable error states from docs/navigation-flows.md's "Error Code Mapping" — only the
/// styling axis (color/icon-ish emphasis), never a source of truth for what actually happened;
/// the message/action text is always supplied by the page that knows the real context.
/// </summary>
public enum ErrorStateKind
{
    Generic,
    Unauthorized,
    Forbidden,
    NotFound,
    Expired,
    RateLimited
}
