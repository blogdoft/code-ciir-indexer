using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Ciir.Indexer.Api.Authentication;

/// <summary>Registers Keycloak-issued JWT bearer authentication (auth spec, "Keycloak configurado").</summary>
public static class KeycloakAuthenticationExtensions
{
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
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = options.Audience.Length > 0,
                    ValidAudience = options.Audience.Length > 0 ? options.Audience : null,
                };
                bearer.Events = new JwtBearerEvents { OnChallenge = WriteProblemDetailsAsync };
            });

        services.AddAuthorization(authorization =>
            authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    // Replaces the handler's default body-less 401 with the same application/problem+json shape
    // every other client error in this API uses. The detail is deliberately generic: it must not
    // reveal why a token was rejected (signature, issuer, expiry, ...).
    private static Task WriteProblemDetailsAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;

        return Results.Problem(
            type: $"https://httpstatuses.io/{StatusCodes.Status401Unauthorized}",
            title: ReasonPhrases.GetReasonPhrase(StatusCodes.Status401Unauthorized),
            detail: "A valid access token is required. Send it in the 'Authorization: Bearer <token>' header.",
            statusCode: StatusCodes.Status401Unauthorized,
            instance: context.Request.Path)
            .ExecuteAsync(context.HttpContext);
    }
}
