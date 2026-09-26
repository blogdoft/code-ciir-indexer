namespace Ciir.Indexer.Api.Authentication;

/// <summary>Registers the client behind <c>POST /api/indexer/auth/token</c> (token gateway spec).</summary>
public static class KeycloakTokenGatewayExtensions
{
    private static readonly TimeSpan TokenRequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registers <see cref="IKeycloakTokenClient"/>. Only call this when authentication is enabled:
    /// without the client, the token endpoint answers <c>404</c> because there is no token to issue.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="options">The validated Keycloak options (see <see cref="KeycloakOptions.FromConfiguration"/>).</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddKeycloakTokenGateway(this IServiceCollection services, KeycloakOptions options)
    {
        services.AddSingleton(options);

        var client = services.AddHttpClient<IKeycloakTokenClient, KeycloakTokenClient>(http => http.Timeout = TokenRequestTimeout);

        // Same opt-in as the JWT bearer backchannel (see KeycloakOptions.SkipCertificateValidation).
        if (options.SkipCertificateValidation)
        {
#pragma warning disable S4830 // deliberate, opt-in via SkipCertificateValidation - see KeycloakOptions
            client.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
#pragma warning restore S4830
        }

        return services;
    }
}
