using Bunit;
using FileSharing.Web.Models;
using FileSharing.Web.Services;
using FileSharing.Web.Services.Notifications;
using FileSharing.Web.Tests.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileSharing.Web.Tests.Components;

/// <summary>
/// Base for bUnit component tests — wires up the same services Program.cs registers, but with
/// a fake HttpMessageHandler instead of a real Api, and pointed at a closed local port
/// (loopback, no listener) so a real SignalRNotificationService.StartAsync() call fails
/// immediately (connection refused) rather than hanging on a DNS/connect timeout.
/// </summary>
public class WebComponentTestContext : TestContext
{
    protected const string FakeApiBaseUrl = "http://127.0.0.1:65500/";

    public AuthTokenProvider TokenProvider { get; }
    public ApiAuthenticationStateProvider AuthStateProvider { get; }
    public RoutedFakeHttpMessageHandler Handler { get; }

    public WebComponentTestContext(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this(new RoutedFakeHttpMessageHandler(responder))
    {
    }

    /// <summary>
    /// Async overload — lets a test hold a response open (e.g. via a TaskCompletionSource) to
    /// observe a component's in-flight/loading state before completing it.
    /// </summary>
    public WebComponentTestContext(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : this(new RoutedFakeHttpMessageHandler(responder))
    {
    }

    private WebComponentTestContext(RoutedFakeHttpMessageHandler handler)
    {
        Handler = handler;

        var httpClient = new HttpClient(Handler) { BaseAddress = new Uri(FakeApiBaseUrl) };
        TokenProvider = new AuthTokenProvider();
        AuthStateProvider = new ApiAuthenticationStateProvider();

        Services.AddSingleton(TokenProvider);
        Services.AddSingleton(AuthStateProvider);
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(AuthStateProvider);
        Services.AddSingleton(new FileSharingApiClient(httpClient, TokenProvider));
        Services.AddSingleton(new ToastService());
        Services.AddSingleton(new NotificationInboxService());
        Services.AddSingleton(new SignalRNotificationService(
            Options.Create(new ApiSettings { BaseUrl = FakeApiBaseUrl }),
            TokenProvider,
            NullLogger<SignalRNotificationService>.Instance));

        Services.AddAuthorizationCore();
        // bUnit's TestContext preregisters a placeholder IAuthorizationService that always
        // throws (forcing test authors to opt in to a specific auth setup) — bUnit's own
        // AddTestAuthorization()/AddTestAuthorization() helper would also replace our custom
        // ApiAuthenticationStateProvider with its own fake one, which we don't want here (this
        // context's whole point is exercising the real ApiAuthenticationStateProvider). A real
        // DefaultAuthorizationService, added after AddAuthorizationCore's TryAdd-based
        // registrations, overrides just that placeholder without touching anything else.
        Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationService, Microsoft.AspNetCore.Authorization.DefaultAuthorizationService>();
        Services.AddCascadingAuthenticationState();
    }
}
