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
    private readonly IValidator<InitiateUploadRequest> _initiateUploadValidator;

    public FilesController(IFileUploadService fileUploadService, IValidator<InitiateUploadRequest> initiateUploadValidator)
    {
        _fileUploadService = fileUploadService;
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
}
