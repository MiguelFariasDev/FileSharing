using FileSharing.Web.Components;
using FileSharing.Web.Extensions;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplicationServices(builder.Configuration);

var app = builder.Build();

// Etapa 14: mesma justificativa do FileSharing.Api — necessário para que UseHttpsRedirection
// abaixo não entre em loop de redirecionamento atrás de um Application Load Balancer (o
// tráfego ALB→ECS chega como HTTP simples; sem isto, a aplicação nunca saberia que o cliente
// original já usou HTTPS). Sem efeito em desenvolvimento local (sem proxy na frente).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Liveness only (a Web não tem dependência de leitura/escrita própria — só chama a Api, cuja
// própria saúde já é verificada em /health/ready no lado dela) — usado pelo health check do
// Target Group do ALB/ECS. Antes de qualquer middleware de segurança/HTTPS redirect: um
// orquestrador não deve precisar de HTTPS para checar liveness.
app.MapGet("/health/live", () => Results.Ok("Healthy")).AllowAnonymous();

// Applied first so every response — including error pages — carries these headers.
// 'unsafe-inline' is required in two narrow, documented spots: script-src because Blazor's
// <ImportMap /> component emits an inline <script type="importmap"> with no way to attach a
// nonce/hash to it, and style-src because a couple of components (ToastContainer, FileLinkCell)
// use small inline style="" attributes for one-off spacing/z-index — neither is ever built from
// user-controlled input. connect-src allows ws:/wss: for the Blazor Server circuit's own
// SignalR/WebSocket connection back to this same origin. Every other resource type stays
// same-origin only — the app never loads anything from an external CDN.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'");
    await next();
});

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
