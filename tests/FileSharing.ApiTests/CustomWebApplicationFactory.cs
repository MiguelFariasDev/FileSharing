using FileSharing.Application.Abstractions.Storage;
using FileSharing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace FileSharing.ApiTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecretKey = "integration-test-signing-key-with-enough-entropy-0123456789";

    /// <summary>
    /// Storage is mocked instead of hitting a real S3/LocalStack endpoint from these
    /// HTTP-pipeline tests — the S3-specific implementation is exercised separately
    /// against LocalStack in FileSharing.IntegrationTests. Reconfigure per test with
    /// <c>FileStorageServiceMock.Setup(...)</c>; it is a singleton so state persists
    /// across requests within a test.
    /// </summary>
    public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();

    public CustomWebApplicationFactory()
    {
        FileStorageServiceMock
            .Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUploadUrl("https://mock-s3.test/upload", DateTimeOffset.UtcNow.AddMinutes(15)));

        FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StorageObjectMetadata?)null);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "FileSharing.Tests",
                ["Jwt:Audience"] = "FileSharing.Api.Tests",
                ["Jwt:SecretKey"] = TestJwtSecretKey,
                ["Jwt:ExpirationMinutes"] = "60"
            });
        });

        var databaseName = $"FileSharingTests-{Guid.NewGuid()}";

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor is not null)
                services.Remove(descriptor);

            // O lifetime padrão de DbContextOptions<T> é Scoped, então esta ação é reexecutada a
            // cada novo scope (cada requisição HTTP) — o nome do banco precisa ser fixo fora da
            // lambda, senão cada requisição acabaria enxergando um banco InMemory diferente.
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.RemoveAll<IFileStorageService>();
            services.AddSingleton(FileStorageServiceMock.Object);
        });
    }
}
