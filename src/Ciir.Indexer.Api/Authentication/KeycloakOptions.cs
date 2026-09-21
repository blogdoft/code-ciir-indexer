namespace Ciir.Indexer.Api.Authentication;

/// <summary>
/// Strongly typed binding of the "Keycloak" configuration section (auth spec, "Configuração").
/// Authentication is opt-in: <see cref="FromConfiguration"/> returns <see langword="null"/> unless
/// <see cref="Enabled"/> is set, in which case the API stays fully open.
/// </summary>
public sealed class KeycloakOptions
{
    /// <summary>The name of the configuration section these options bind to.</summary>
    public const string SectionName = "Keycloak";

    /// <summary>Gets or sets a value indicating whether authentication is turned on.</summary>
    /// <value><see langword="true"/> to require a Keycloak access token on every endpoint; <see langword="false"/> (the default) leaves the API open and ignores the rest of the section.</value>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the realm's URL, which hosts its OpenID Connect discovery document.</summary>
    /// <value>The realm's URL (e.g. "https://keycloak.example/realms/my-realm"); required when <see cref="Enabled"/> is set.</value>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Gets or sets the audience a token must be issued for.</summary>
    /// <value>When set, the token's <c>aud</c> claim must contain this value; when empty, the audience is not validated.</value>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Gets or sets the Keycloak client this application is registered as, which Swagger UI's "Authorize" button also logs in with.</summary>
    /// <value>The <c>client_id</c> of a public realm client (authorization code + PKCE) shared by the API and Swagger UI; when empty, Swagger UI only accepts a pasted token.</value>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the discovery document/JWKS must be fetched over HTTPS.</summary>
    /// <value><see langword="true"/> (the default) to require HTTPS; only disable against a local, plain-HTTP Keycloak.</value>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Reads the "Keycloak" section and, when authentication is enabled, validates it.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The validated options, or <see langword="null"/> when <see cref="Enabled"/> is not set - in which case the rest of the section is neither read nor validated.</returns>
    /// <exception cref="InvalidOperationException">
    /// Authentication is enabled but <see cref="Authority"/> is blank, is not an absolute http(s) URL,
    /// or is plain HTTP while <see cref="RequireHttpsMetadata"/> is on.
    /// </exception>
    public static KeycloakOptions? FromConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(SectionName).Get<KeycloakOptions>() ?? new KeycloakOptions();
        if (!options.Enabled)
        {
            return null;
        }

        options.Authority = Normalize(options.Authority);
        options.Audience = Normalize(options.Audience);
        options.ClientId = Normalize(options.ClientId);

        if (options.Authority.Length == 0)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:Enabled' is true but '{SectionName}:Authority' is empty. Set the authority (the realm's URL), or set Enabled to false to leave the API open.");
        }

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                $"'{SectionName}:Authority' must be an absolute http(s) URL (the realm's URL), but was '{options.Authority}'.");
        }

        if (authority.Scheme == Uri.UriSchemeHttp && options.RequireHttpsMetadata)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:Authority' uses plain HTTP while '{SectionName}:RequireHttpsMetadata' is true. Use an HTTPS authority, or set RequireHttpsMetadata to false for a local Keycloak.");
        }

        return options;
    }

    // The configuration binder assigns null (not the property's default) to a key that is present
    // with a null value, e.g. "Authority": null in JSON - so a blank value can be null here too.
    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
