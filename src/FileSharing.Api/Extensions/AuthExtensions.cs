using System.Text;
using FileSharing.Api.Hubs;
using FileSharing.Application.Abstractions.Security;
using FileSharing.Application.Services.Auth;
using FileSharing.Application.Validators.Auth;
using FileSharing.Infrastructure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FileSharing.Api.Extensions;

public static class AuthExtensions
{
    public static IServiceCollection AddAuthServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // As opções do JwtBearer dependem de IOptions<JwtOptions>, resolvido apenas quando o
        // handler de autenticação é ativado (após o host terminar de montar a configuração) —
        // ler a configuração diretamente aqui, no momento do registro de serviços, seria cedo
        // demais para refletir overrides aplicados depois (ex.: WebApplicationFactory em testes).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;

                if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey))
                    throw new InvalidOperationException("Jwt:SecretKey não configurada. Utilize User Secrets ou variável de ambiente.");

                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                // SignalR-only fallback, scoped strictly to the notifications hub path. A
                // browser client cannot attach an Authorization header to a WebSocket upgrade
                // (or an EventSource/SSE request), so the JS SignalR client instead sends the
                // token as an "access_token" query string parameter — this is the standard,
                // documented ASP.NET Core pattern for authenticating SignalR over those
                // transports. It never touches the REST authentication path: outside
                // /hubs/notifications, OnMessageReceived leaves context.Token untouched, so
                // JwtBearer falls through to its normal Authorization-header extraction exactly
                // as before this phase.
                bearerOptions.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments(HubEndpoints.Notifications))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
