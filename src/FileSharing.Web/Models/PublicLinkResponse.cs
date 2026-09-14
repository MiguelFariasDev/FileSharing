namespace FileSharing.Web.Models;

/// <summary>
/// Mirrors the JSON shape FilesController.GenerateLink already returns
/// ({ fileId, accessToken, publicUrl }) — a Web-local model rather than a change to the Api
/// contract, since that anonymous-object response is already stable and tested (Etapa 4).
/// </summary>
public record PublicLinkResponse(Guid FileId, string AccessToken, string PublicUrl);
