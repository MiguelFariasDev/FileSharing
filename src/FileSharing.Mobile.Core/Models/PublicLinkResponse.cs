namespace FileSharing.Mobile.Core.Models;

/// <summary>
/// Mirrors the anonymous object FilesController.GenerateLink actually returns
/// (`{ fileId, accessToken, publicUrl }`) — System.Text.Json matches by property name
/// regardless of whether the server-side type was a named DTO or an anonymous object, so this
/// deserializes it exactly like FileSharing.Web's own PublicLinkResponse does.
/// </summary>
public record PublicLinkResponse(Guid FileId, string AccessToken, string PublicUrl);
