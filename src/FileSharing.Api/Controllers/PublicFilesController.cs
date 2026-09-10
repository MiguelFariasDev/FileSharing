using FileSharing.Application.Services.Files;
using FileSharing.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FileSharing.Api.Controllers;

/// <summary>
/// Public, unauthenticated surface for resolving a shared link. No [Authorize] here — the
/// whole point is that whoever holds the token (not necessarily a registered user) can use it.
/// Rate-limited to slow down brute-force token guessing (see Program.cs).
/// </summary>
[ApiController]
[Route("api/public/files")]
[EnableRateLimiting(RateLimiterPolicyNames.PublicFiles)]
public class PublicFilesController : ControllerBase
{
    private readonly IFilePublicLinkService _filePublicLinkService;
    private readonly IFileDownloadService _fileDownloadService;

    public PublicFilesController(IFilePublicLinkService filePublicLinkService, IFileDownloadService fileDownloadService)
    {
        _filePublicLinkService = filePublicLinkService;
        _fileDownloadService = fileDownloadService;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> GetPublicFile(string token, CancellationToken cancellationToken)
    {
        var result = await _filePublicLinkService.GetByAccessTokenAsync(token, cancellationToken);

        // Deliberately the exact same response — no message, no distinguishing detail — for
        // an unknown token, an expired file, and a file that was removed. The token itself is
        // never echoed back or logged.
        if (!result.IsSuccess)
            return NotFound();

        return Ok(result.Value);
    }

    [HttpGet("{token}/download")]
    public async Task<IActionResult> DownloadPublicFile(string token, CancellationToken cancellationToken)
    {
        // Real client IP, not a client-suppliable header: HttpContext.Connection.RemoteIpAddress
        // is the TCP peer address, so a request cannot forge it by sending an arbitrary
        // X-Forwarded-For (which this API does not read). Behind a reverse proxy/ALB this would
        // reflect the proxy's address unless Forwarded Headers Middleware is configured — that
        // middleware is not part of this phase (see docs/security.md).
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // A missing User-Agent must never fail the request, and the value must fit the column
        // Download enforces (Download.MaxUserAgentLength) — both handled here, at the HTTP
        // boundary, rather than letting Domain/Application see an HTTP-header-shaped concern.
        var userAgent = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = "unknown";
        else if (userAgent.Length > Download.MaxUserAgentLength)
            userAgent = userAgent[..Download.MaxUserAgentLength];

        var result = await _fileDownloadService.DownloadAsync(token, ipAddress, userAgent, cancellationToken);

        // Same generic response as GetPublicFile — unknown token, expired file, wrong status,
        // and object missing from storage are all indistinguishable from here.
        if (!result.IsSuccess)
            return NotFound();

        return Ok(result.Value);
    }
}
