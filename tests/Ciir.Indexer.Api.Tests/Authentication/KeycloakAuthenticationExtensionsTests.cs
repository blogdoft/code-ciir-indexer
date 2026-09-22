using Ciir.Indexer.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Authentication;

/// <summary>
/// Verifies <see cref="KeycloakAuthenticationExtensions.AddKeycloakAuthentication"/> wires
/// <see cref="KeycloakOptions.MetadataAddress"/> into the registered <see cref="JwtBearerOptions"/>
/// without disturbing <see cref="JwtBearerOptions.Authority"/> - the actual JWT validation behavior
/// (token accepted/rejected) is covered end-to-end by <see cref="KeycloakAuthenticationTests"/>,
/// which always short-circuits metadata discovery with a fixed <c>Configuration</c>, so it can't see
/// this wiring on its own.
/// </summary>
public sealed class KeycloakAuthenticationExtensionsTests
{
    [Fact]
    public void AddKeycloakAuthentication_WithMetadataAddress_OverridesBearerMetadataAddressButNotAuthority()
    {
        var options = new KeycloakOptions
        {
            Authority = "https://keycloak.example/realms/blogdoft",
            MetadataAddress = "http://keycloak.internal.svc.cluster.local:8080/realms/blogdoft/.well-known/openid-configuration",
            RequireHttpsMetadata = false,
        };

        var bearer = ResolveJwtBearerOptions(options);

        bearer.MetadataAddress.ShouldBe(options.MetadataAddress);
        bearer.Authority.ShouldBe(options.Authority);
        bearer.RequireHttpsMetadata.ShouldBeFalse();
    }

    [Fact]
    public void AddKeycloakAuthentication_WithoutMetadataAddress_DerivesItFromAuthorityAsBefore()
    {
        var options = new KeycloakOptions { Authority = "https://keycloak.example/realms/blogdoft" };

        var bearer = ResolveJwtBearerOptions(options);

        // Not overridden: JwtBearerPostConfigureOptions derives it from Authority itself, same as
        // before this option existed.
        bearer.MetadataAddress.ShouldBe("https://keycloak.example/realms/blogdoft/.well-known/openid-configuration");
        bearer.Authority.ShouldBe(options.Authority);
    }

    private static JwtBearerOptions ResolveJwtBearerOptions(KeycloakOptions options)
    {
        var services = new ServiceCollection();
        services.AddKeycloakAuthentication(options);
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
