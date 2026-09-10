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
    private readonly IValidator<InitiateUploadRequest> _initiateUploadValidator;

    public FilesController(
        IFileUploadService fileUploadService,
        IFilePublicLinkService filePublicLinkService,
        IValidator<InitiateUploadRequest> initiateUploadValidator)
    {
        _fileUploadService = fileUploadService;
        _filePublicLinkService = filePublicLinkService;
        _initiateUploadValidator = initiateUploadValidator;
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

        var result = await _fileUploadService.InitiateUploadAsync(userId, request, cancellationToken);
        if (!result.IsSuccess)
            return BadRequest(new { message = result.Error });

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteUpload(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _fileUploadService.CompleteUploadAsync(userId, id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.FailureReason == CompleteUploadFailureReason.NotFound
                ? NotFound(new { message = result.Error })
                : Conflict(new { message = result.Error });
        }

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/link")]
    public async Task<IActionResult> GenerateLink(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var result = await _filePublicLinkService.GenerateLinkAsync(userId, id, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.FailureReason == GenerateLinkFailureReason.NotFound
                ? NotFound(new { message = result.Error })
                : Conflict(new { message = result.Error });
        }

        // publicUrl is derived from the incoming request's own scheme/host — never a
        // hardcoded production domain — so it works unchanged across local, staging and prod.
        var publicUrl = Url.Action(
            action: nameof(PublicFilesController.GetPublicFile),
            controller: "PublicFiles",
            values: new { token = result.Value!.AccessToken },
            protocol: Request.Scheme,
            host: Request.Host.Value);

        return Ok(new
        {
            fileId = result.Value.FileId,
            accessToken = result.Value.AccessToken,
            publicUrl
        });
    }
}
