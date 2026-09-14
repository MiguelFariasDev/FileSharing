using FileSharing.Web.Components;
using FileSharing.Web.Models;
using FileSharing.Web.Services;
using FileSharing.Web.Services.Notifications;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// [Authorize] on a routed Razor component (Dashboard.razor) attaches authorization metadata to
// its endpoint, which ASP.NET Core's endpoint routing enforces via AuthorizationMiddleware even
// without an explicit UseAuthorization() call. For the very first (static, pre-circuit) request
// to a protected page, that middleware's "not authorized" path calls ChallengeAsync() against
// the default scheme — with no scheme registered at all this 500s; with none *configured* to
// redirect anywhere it fails identically. The cookie scheme below is never actually used to
// establish real identity (this app's own identity check is ApiAuthenticationStateProvider,
// entirely separate and JWT-driven) — it exists solely so a static request to a protected page
// gets a real 302 to /login instead of a 500, before any interactive circuit (and this app's
// own AuthorizeRouteView/RedirectToLogin) even exists. No cookie is ever issued because nothing
// in this app calls SignInAsync.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.LoginPath = "/login");
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<ApiSettings>(builder.Configuration.GetSection(ApiSettings.SectionName));

// AuthTokenProvider/ApiAuthenticationStateProvider are Scoped so each Blazor Server circuit
// (effectively, each browser tab's session) gets its own JWT/identity — never shared across
// users, never touching the browser (see AuthTokenProvider remarks).
builder.Services.AddScoped<AuthTokenProvider>();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<ApiAuthenticationStateProvider>());

builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<SignalRNotificationService>();

// The only HttpClient in this project — every Api call goes through FileSharingApiClient.
builder.Services.AddHttpClient<FileSharingApiClient>((sp, client) =>
{
    var apiSettings = sp.GetRequiredService<IOptions<ApiSettings>>().Value;

    if (string.IsNullOrWhiteSpace(apiSettings.BaseUrl))
        throw new InvalidOperationException("Api:BaseUrl não configurada. Configure appsettings.{Environment}.json.");

    client.BaseAddress = new Uri(apiSettings.BaseUrl);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
