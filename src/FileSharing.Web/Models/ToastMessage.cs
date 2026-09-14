namespace FileSharing.Web.Models;

public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}

public record ToastMessage(Guid Id, string Text, ToastLevel Level);
