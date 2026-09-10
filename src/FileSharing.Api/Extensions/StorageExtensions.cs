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

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var awsSection = configuration.GetSection("AWS");
            var serviceUrl = awsSection["ServiceURL"];
            var region = awsSection["Region"] ?? configuration.GetSection(FileStorageOptions.SectionName)["Region"];

            var s3Config = new AmazonS3Config();

            if (!string.IsNullOrWhiteSpace(region))
                s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);

            // Quando configurado (LocalStack/dev), aponta o cliente para um endpoint S3
            // customizado no lugar do AWS real — a única diferença entre ambientes é esta
            // configuração; o restante do código (Application/Infrastructure) é idêntico.
            //
            // A partir do AWSSDK.S3 v4, AmazonS3Config.ServiceURL sozinho não é mais
            // suficiente para redirecionar as chamadas (o SDK passou a resolver o endpoint
            // por outro caminho); a variável de ambiente AWS_ENDPOINT_URL_S3 é o mecanismo
            // que o SDK realmente honra. ServiceURL continua setado abaixo apenas para que
            // S3FileStorageService saiba escolher http/https ao montar a presigned URL.
            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_S3", serviceUrl);
                s3Config.ServiceURL = serviceUrl;
                s3Config.ForcePathStyle = true;
            }

            var accessKey = awsSection["AccessKey"];
            var secretKey = awsSection["SecretKey"];

            return !string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey)
                ? new AmazonS3Client(accessKey, secretKey, s3Config)
                : new AmazonS3Client(s3Config); // cadeia padrão de credenciais (IAM role, variáveis de ambiente, etc.)
        });

        services.AddScoped<IFileStorageService, S3FileStorageService>();
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IFilePublicLinkService, FilePublicLinkService>();
        services.AddScoped<IFileDownloadService, FileDownloadService>();

        services.AddValidatorsFromAssemblyContaining<InitiateUploadRequestValidator>();

        return services;
    }
}
