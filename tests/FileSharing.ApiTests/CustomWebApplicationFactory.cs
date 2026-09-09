using FileSharing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileSharing.ApiTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecretKey = "integration-test-signing-key-with-enough-entropy-0123456789";

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
        });
    }
}
