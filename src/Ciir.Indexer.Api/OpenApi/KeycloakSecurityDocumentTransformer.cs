using Ciir.Indexer.Api.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Declares how a caller authenticates on the OpenAPI document, so Swagger UI offers an "Authorize"
/// button: always a <c>Bearer</c> (JWT) scheme where an access token is pasted, plus - when
/// <see cref="KeycloakOptions.ClientId"/> is configured - an <c>OAuth2</c> authorization-code
/// scheme that sends the user to the Keycloak login page instead. Either satisfies the global
/// security requirement. Only registered when Keycloak authentication is enabled (auth spec,
/// "OpenAPI / Swagger UI" and "Login via Keycloak no Swagger UI"); without it the document carries
/// no security information.
/// </summary>
/// <param name="options">The validated Keycloak options, from which the realm's login/token URLs are derived.</param>
internal sealed class KeycloakSecurityDocumentTransformer(KeycloakOptions options) : IOpenApiDocumentTransformer
{
    /// <summary>The key the paste-a-token scheme is registered under in the document's security schemes.</summary>
    internal const string BearerSchemeName = "Bearer";

    /// <summary>The key the Keycloak login scheme is registered under in the document's security schemes.</summary>
    internal const string OAuth2SchemeName = "OAuth2";

    /// <summary>The only scope requested: enough for Keycloak to issue an ID/access token pair for the logged-in user.</summary>
    internal const string OpenIdScope = "openid";

    /// <summary>Adds the security schemes, and the global requirement satisfied by any of them, to <paramref name="document"/>.</summary>
    /// <param name="document">The OpenAPI document being built.</param>
    /// <param name="context">Unused - the schemes are derived from the Keycloak options, not the API description.</param>
    /// <param name="cancellationToken">Unused - this transformer does no asynchronous work.</param>
    /// <returns>A completed task; this transformer mutates <paramref name="document"/> synchronously.</returns>
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Security ??= [];

        document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Keycloak access token. Paste the token only, without the 'Bearer ' prefix.",
        };
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = [],
        });

        if (options.ClientId.Length > 0)
        {
            var openIdConnect = $"{options.Authority.TrimEnd('/')}/protocol/openid-connect";
            document.Components.SecuritySchemes[OAuth2SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "Log in through Keycloak (authorization code flow with PKCE).",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{openIdConnect}/auth"),
                        TokenUrl = new Uri($"{openIdConnect}/token"),
                        Scopes = new Dictionary<string, string> { [OpenIdScope] = "OpenID Connect login" },
                    },
                },
            };
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(OAuth2SchemeName, document)] = [OpenIdScope],
            });
        }

        return Task.CompletedTask;
    }
}
