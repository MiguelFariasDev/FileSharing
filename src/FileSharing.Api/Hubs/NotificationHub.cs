using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FileSharing.Api.Hubs;

/// <summary>
/// Authenticated SignalR endpoint at /hubs/notifications — no anonymous connection is ever
/// accepted (see Program.cs UseAuthentication/UseAuthorization, applied before endpoint
/// routing). The Hub itself is intentionally just a connection point: it carries no business
/// logic, no database access, and defines no client-callable methods — the server pushes
/// events to it (see SignalRFileDownloadNotifier), clients only ever receive.
///
/// A connected user's identity comes exclusively from their JWT's "sub" claim, never from
/// anything the client sends over the wire (see SubClaimUserIdProvider) — a client cannot
/// choose which user's notifications it receives.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
}
