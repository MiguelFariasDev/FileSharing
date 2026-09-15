using Amazon;
using Amazon.S3;
using FileSharing.Application.Abstractions.Storage;
using FileSharing.Application.Services.Files;
using FileSharing.Application.Validators.Files;
using FileSharing.Infrastructure.Storage;
using FluentValidation;

namespace FileSharing.Api.Extensions;

public static class StorageExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        var awsSection = configuration.GetSection("AWS");
        var serviceUrl = awsSection["ServiceURL"];
        var region = awsSection["Region"] ?? configuration.GetSection(FileStorageOptions.SectionName)["Region"];
        var accessKey = awsSection["AccessKey"];
        var secretKey = awsSection["SecretKey"];

        // Only relevant in Docker Compose (dev): the Api reaches LocalStack over the internal
        // service name ("http://localstack:4566"), but a presigned URL handed back to a browser
        // on the host or a Mobile emulator/device must point at an address *they* can reach
        // ("http://localhost:4566"). In every other environment (plain `dotnet run`, real AWS)
        // this is left unset and both clients below end up identically configured — see
        // S3FileStorageService's remarks on why the URL is signed for the public endpoint rather
        // than rewritten after the fact.
        var publicServiceUrl = awsSection["PublicServiceURL"];

        // AWS_ENDPOINT_URL_S3 is a process-wide environment variable, set exactly once here —
        // never inside BuildS3Client, and never using publicServiceUrl. It only governs where
        // *real* S3 API calls (HeadObject, DeleteObject, ...) actually go, which only the main
        // client ever makes; the presign client below never issues a network call (GetPreSignedURL
        // is local), so it has no need for this variable and must never overwrite it — doing so
        // would risk the main client's real requests silently starting to target the public
        // endpoint too (unreachable from inside a container) depending on when the SDK resolves it.
        if (!string.IsNullOrWhiteSpace(serviceUrl))
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_S3", serviceUrl);

        services.AddSingleton<IAmazonS3>(_ => BuildS3Client(serviceUrl, region, accessKey, secretKey));

        services.AddKeyedSingleton<IAmazonS3>(
            FileSharing.Infrastructure.Storage.S3FileStorageService.PresignClientKey,
            (_, _) => BuildS3Client(publicServiceUrl ?? serviceUrl, region, accessKey, secretKey));

        services.AddScoped<IFileStorageService, S3FileStorageService>();
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IFilePublicLinkService, FilePublicLinkService>();
        services.AddScoped<IFileDownloadService, FileDownloadService>();
        services.AddScoped<IFileQueryService, FileQueryService>();

        services.AddValidatorsFromAssemblyContaining<InitiateUploadRequestValidator>();

        return services;
    }

    /// <summary>
    /// Only sets <see cref="AmazonS3Config.ServiceURL"/>/<see cref="AmazonS3Config.ForcePathStyle"/>
    /// — deliberately never touches the AWS_ENDPOINT_URL_S3 environment variable (see the one,
    /// single place that happens in <see cref="AddFileStorage"/> above, and why). ServiceURL alone
    /// is enough for what this client is ever used for: choosing http/https and the host baked
    /// into a presigned URL (S3FileStorageService), or — for the main client — as a fallback the
    /// SDK still reads in code paths that don't go through the v4 endpoint-resolution pipeline.
    /// </summary>
    private static AmazonS3Client BuildS3Client(string? serviceUrl, string? region, string? accessKey, string? secretKey)
    {
        var s3Config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(region))
            s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            s3Config.ServiceURL = serviceUrl;
            s3Config.ForcePathStyle = true;
        }

        return !string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(accessKey, secretKey, s3Config)
            : new AmazonS3Client(s3Config); // cadeia padrão de credenciais (IAM role, variáveis de ambiente, etc.)
    }
}
