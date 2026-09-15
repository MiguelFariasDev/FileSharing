using FileSharing.Api.Extensions;
using FileSharing.Application.DTOs.Files;
using FileSharing.Application.Services.Files;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileSharing.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IFileUploadService _fileUploadService;
    private readonly IFilePublicLinkService _filePublicLinkService;
    private readonly IFileQueryService _fileQueryService;
    private readonly IValidator<InitiateUploadRequest> _initiateUploadValidator;

    public FilesController(
        IFileUploadService fileUploadService,
        IFilePublicLinkService filePublicLinkService,
        IFileQueryService fileQueryService,
        IValidator<InitiateUploadRequest> initiateUploadValidator)
    {
        _fileUploadService = fileUploadService;
        _filePublicLinkService = filePublicLinkService;
        _fileQueryService = fileQueryService;
        _initiateUploadValidator = initiateUploadValidator;
    }

    /// <summary>
    /// Backs the Etapa 8 dashboard's file list — every field a dashboard needs to show
    /// name/type/size/status/dates/download count/whether a public link exists, and nothing
    /// more (never the access token or its hash).
    /// </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMyFiles(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var files = await _fileQueryService.GetMyFilesAsync(userId, cancellationToken);
        return Ok(files);
    }

    /// <summary>
    /// Owner-only download history for one file — same generic 404 for "does not exist" and
    /// "belongs to someone else" as every other owner-scoped endpoint here.
    /// </summary>
    [HttpGet("{id:guid}/downloads")]
    public async Task<IActionResult> GetDownloadHistory(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var history = await _fileQueryService.GetDownloadHistoryAsync(userId, id, cancellationToken);
        return Ok(history);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> InitiateUpload(InitiateUploadRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var validation = await _initiateUploadValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState);
            return ValidationProblem(ModelState);
        }

        var response = await _fileUploadService.InitiateUploadAsync(userId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteUpload(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var response = await _fileUploadService.CompleteUploadAsync(userId, id, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/link")]
    public async Task<IActionResult> GenerateLink(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var response = await _filePublicLinkService.GenerateLinkAsync(userId, id, cancellationToken);

        // publicUrl is derived from the incoming request's own scheme/host — never a
        // hardcoded production domain — so it works unchanged across local, staging and prod.
        var publicUrl = Url.Action(
            action: nameof(PublicFilesController.GetPublicFile),
            controller: "PublicFiles",
            values: new { token = response.AccessToken },
            protocol: Request.Scheme,
            host: Request.Host.Value);

        return Ok(new
        {
            fileId = response.FileId,
            accessToken = response.AccessToken,
            publicUrl
        });
    }
}
