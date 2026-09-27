using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Ciir.Indexer.Api.Authentication;

/// <summary>Registers Keycloak-issued JWT bearer authentication (auth spec, "Keycloak configurado").</summary>
public static class KeycloakAuthenticationExtensions
{
    private const string ChallengeLoggerCategory = "Ciir.Indexer.Api.Authentication.Challenge";

    /// <summary>
    /// Requires a valid Keycloak access token on every endpoint by default: registers the JWT bearer
    /// scheme against the realm and sets the authorization fallback policy to "authenticated user",
    /// so a route is protected unless it explicitly opts out with <c>AllowAnonymous</c> - rather than
    /// open unless someone remembers an attribute.
    /// </summary>
    /// <param name="services">The service collection to add authentication to.</param>
    /// <param name="options">The validated Keycloak options (see <see cref="KeycloakOptions.FromConfiguration"/>).</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, KeycloakOptions options)
    {
        services.AddSingleton(options);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.Authority = options.Authority;

                // MetadataAddress, when set, overrides *where the JWKS/discovery document come from*
                // only - Authority stays the public URL used for issuer validation (Keycloak's
                // discovery document reports its own public issuer no matter which address served
                // it) and for the Swagger "Authorize" button's login endpoint.
                if (options.MetadataAddress.Length > 0)
                {
                    bearer.MetadataAddress = options.MetadataAddress;
                }

                bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
                bearer.MapInboundClaims = false;

                // See KeycloakOptions.SkipCertificateValidation: only needed when the JWKS host's CA
                // isn't trusted by this container and that trust can't be fixed directly (yet).
                if (options.SkipCertificateValidation)
                {
#pragma warning disable S4830 // deliberate, opt-in via SkipCertificateValidation - see KeycloakOptions
                    bearer.BackchannelHttpHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    };
#pragma warning restore S4830
                }

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = options.Audience.Length > 0,
                    ValidAudience = options.Audience.Length > 0 ? options.Audience : null,
                };
                bearer.Events = new JwtBearerEvents { OnChallenge = RejectAsync };
            });

        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    // A body-less 401 (csharp-api: 401/403/404/5xx carry no response body - only application logs).
    // The bare "Bearer" challenge and the empty body deliberately reveal nothing about why a token
    // was rejected (signature, issuer, expiry, ...); the reason goes to the log instead.
    private static Task RejectAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(ChallengeLoggerCategory)
            .LogWarning(
                context.AuthenticateFailure,
                "Rejected {Method} {Path} with 401: {Error} {ErrorDescription}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Error ?? "no bearer token",
                context.ErrorDescription);

        return Task.CompletedTask;
    }
}
