using FileSharing.Api.Extensions;
using Microsoft.AspNetCore.SignalR;

namespace FileSharing.Api.Hubs;

/// <summary>
/// Makes <c>Clients.User(userId)</c> address connections by the JWT's "sub" claim — SignalR's
/// default <see cref="IUserIdProvider"/> reads <c>ClaimTypes.NameIdentifier</c> instead, which
/// this API's tokens never populate (<c>JwtBearerOptions.MapInboundClaims = false</c> keeps
/// claim names exactly as issued — "sub", not the long ClaimTypes URI — see AuthExtensions).
/// Without this override, every hub connection's UserIdentifier would be null and
/// Clients.User(...) would silently reach nobody.
///
/// Reuses <see cref="ClaimsPrincipalExtensions.TryGetUserId"/> — the same claim-parsing logic
/// already used to resolve the caller's identity for every REST endpoint — so a connection's
/// identifier and an owner id looked up from the database always compare equal after both are
/// formatted via the same <see cref="Guid.ToString()"/>, regardless of how the token's "sub"
/// string happened to be cased.
/// </summary>
public class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.TryGetUserId(out var userId) ? userId.ToString() : null;
}
