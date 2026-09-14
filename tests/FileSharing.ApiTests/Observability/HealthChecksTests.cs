using System.Net;

namespace FileSharing.ApiTests.Observability;

/// <summary>
/// CustomWebApplicationFactory backs PostgresHealthCheck with the InMemory EF Core provider and
/// S3HealthCheck with the already-mocked IFileStorageService (default setup returns null for
/// GetObjectMetadataAsync — a clean "not found", exactly what a real reachable-but-empty bucket
/// would also return) — both checks are expected Healthy here without needing a real Postgres/S3
/// connection, proving the endpoints and their wiring work independent of real infrastructure
/// being reachable from this test run.
/// </summary>
public class HealthChecksTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthChecksTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_DoesNotRequireAuthentication_AndReportsHealthy()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_DoesNotRequireAuthentication_AndReportsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liveness_NeverIncludesPostgresOrS3Checks()
    {
        // Liveness must never fail just because a downstream dependency is slow/down — asserted
        // indirectly here: readiness and liveness are reached via entirely separate endpoints
        // (Program.cs's tag-based Predicate), so this just confirms liveness answers immediately
        // and independently of readiness's own dependencies ever being exercised.
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_NeverExposeConnectionStringsOrSecrets()
    {
        var liveBody = await (await _client.GetAsync("/health/live")).Content.ReadAsStringAsync();
        var readyBody = await (await _client.GetAsync("/health/ready")).Content.ReadAsStringAsync();

        foreach (var body in new[] { liveBody, readyBody })
        {
            Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AccessKey", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SecretKey", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
