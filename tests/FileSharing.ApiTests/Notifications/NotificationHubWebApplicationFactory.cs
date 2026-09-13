using FileSharing.Application.Abstractions.Storage;
using FileSharing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace FileSharing.ApiTests.Notifications;

/// <summary>
/// A separate WebApplicationFactory from CustomWebApplicationFactory, deliberately: that one
/// mocks IFileDownloadNotifier (to unit-test the download flow's own logic without a live
/// SignalR connection), which is exactly what these tests need to NOT be mocked — they exercise
/// the real SignalRFileDownloadNotifier / NotificationHub / SubClaimUserIdProvider end to end,
/// over a real (test-server-backed) SignalR connection. Storage is still mocked, same as
/// CustomWebApplicationFactory, since S3 isn't what this fixture is testing.
/// </summary>
public class NotificationHubWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecretKey = CustomWebApplicationFactory.TestJwtSecretKey;

    public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();

    public NotificationHubWebApplicationFactory()
    {
        FileStorageServiceMock
            .Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedUploadUrl("https://mock-s3.test/upload", DateTimeOffset.UtcNow.AddMinutes(15)));

        FileStorageServiceMock
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StorageObjectMetadata?)null);

        FileStorageServiceMock
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        FileStorageServiceMock
            .Setup(s => s.CreatePresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PresignedDownloadUrl("https://mock-s3.test/download", DateTimeOffset.UtcNow.AddMinutes(5)));
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

        var databaseName = $"FileSharingSignalRTests-{Guid.NewGuid()}";

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.RemoveAll<IFileStorageService>();
            services.AddSingleton(FileStorageServiceMock.Object);

            // IFileDownloadNotifier is intentionally left as the real SignalRFileDownloadNotifier
            // registered by Program.cs — that is the whole point of this fixture.
        });
    }
}
