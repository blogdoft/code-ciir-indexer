using Ciir.Indexer.Api.Authentication;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Authentication;

public sealed class KeycloakOptionsTests
{
    [Fact]
    public void FromConfiguration_NoKeycloakSection_ReturnsNull()
    {
        KeycloakOptions.FromConfiguration(Configure([])).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void FromConfiguration_NotEnabled_ReturnsNullEvenWithTheRealmFullyConfigured(string? enabled)
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = enabled,
            ["Keycloak:Authority"] = "https://keycloak.example/realms/blogdoft",
            ["Keycloak:Audience"] = "ciir-indexer",
            ["Keycloak:ClientId"] = "swagger",
        });

        KeycloakOptions.FromConfiguration(configuration).ShouldBeNull();
    }

    [Fact]
    public void FromConfiguration_NotEnabled_DoesNotValidateTheRestOfTheSection()
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "false",
            ["Keycloak:Authority"] = "not-a-url",
        });

        KeycloakOptions.FromConfiguration(configuration).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromConfiguration_EnabledWithoutAuthority_Throws(string? authority)
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "true",
            ["Keycloak:Authority"] = authority,
        });

        var exception = Should.Throw<InvalidOperationException>(() => KeycloakOptions.FromConfiguration(configuration));

        exception.Message.ShouldContain("Keycloak:Authority");
    }

    [Theory]
    [InlineData("realms/blogdoft")]
    [InlineData("ftp://keycloak.example/realms/blogdoft")]
    public void FromConfiguration_EnabledWithAuthorityNotAnAbsoluteHttpUrl_Throws(string authority)
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "true",
            ["Keycloak:Authority"] = authority,
        });

        Should.Throw<InvalidOperationException>(() => KeycloakOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void FromConfiguration_EnabledWithHttpAuthorityWhileRequiringHttpsMetadata_Throws()
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "true",
            ["Keycloak:Authority"] = "http://localhost:8080/realms/blogdoft",
        });

        var exception = Should.Throw<InvalidOperationException>(() => KeycloakOptions.FromConfiguration(configuration));

        exception.Message.ShouldContain("RequireHttpsMetadata");
    }

    [Fact]
    public void FromConfiguration_EnabledWithHttpAuthorityWithoutRequiringHttpsMetadata_ReturnsOptions()
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "true",
            ["Keycloak:Authority"] = "http://localhost:8080/realms/blogdoft",
            ["Keycloak:RequireHttpsMetadata"] = "false",
        });

        var options = KeycloakOptions.FromConfiguration(configuration);

        options.ShouldNotBeNull();
        options.RequireHttpsMetadata.ShouldBeFalse();
    }

    [Fact]
    public void FromConfiguration_EnabledWithFullConfiguration_ReturnsTrimmedOptions()
    {
        var configuration = Configure(new()
        {
            ["Keycloak:Enabled"] = "true",
            ["Keycloak:Authority"] = "  https://keycloak.example/realms/blogdoft ",
            ["Keycloak:Audience"] = " ciir-indexer ",
            ["Keycloak:ClientId"] = " swagger ",
        });

        var options = KeycloakOptions.FromConfiguration(configuration);

        options.ShouldNotBeNull();
        options.Enabled.ShouldBeTrue();
        options.Authority.ShouldBe("https://keycloak.example/realms/blogdoft");
        options.Audience.ShouldBe("ciir-indexer");
        options.ClientId.ShouldBe("swagger");
        options.RequireHttpsMetadata.ShouldBeTrue();
    }

    private static IConfiguration Configure(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
