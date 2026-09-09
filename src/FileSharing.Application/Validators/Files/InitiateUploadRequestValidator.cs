using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Common;
using FileSharing.Application.DTOs.Files;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace FileSharing.Application.Validators.Files;

public class InitiateUploadRequestValidator : AbstractValidator<InitiateUploadRequest>
{
    private const string ZipContentType = "application/zip";

    public InitiateUploadRequestValidator(IOptions<FileStorageOptions> storageOptions)
    {
        var maxFileSizeBytes = storageOptions.Value.MaxFileSizeBytes;

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(FileTypePolicy.IsContentTypeAllowed)
            .WithMessage("Content-Type não permitido.");

        RuleFor(x => x.SizeBytes)
            .GreaterThan(0)
            .WithMessage("O tamanho do arquivo deve ser maior que zero.")
            .LessThanOrEqualTo(maxFileSizeBytes)
            .WithMessage("O tamanho do arquivo excede o limite permitido.");

        When(x => !string.IsNullOrEmpty(x.FileName) && FileTypePolicy.IsContentTypeAllowed(x.ContentType), () =>
        {
            RuleFor(x => x)
                .Must(x => FileTypePolicy.IsFileNameConsistentWithContentType(x.FileName, x.ContentType))
                .WithMessage("A extensão do arquivo não corresponde ao Content-Type informado.")
                .WithName(nameof(InitiateUploadRequest.FileName));
        });

        RuleFor(x => x)
            .Must(x => !x.IsFolder || x.ContentType.Equals(ZipContentType, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Uploads de pasta devem ser enviados como application/zip.")
            .WithName(nameof(InitiateUploadRequest.ContentType));
    }
}
