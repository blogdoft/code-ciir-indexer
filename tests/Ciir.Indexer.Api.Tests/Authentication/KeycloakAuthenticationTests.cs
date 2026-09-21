using Ciir.Indexer.Api.Authentication;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using System.Net;

namespace Ciir.Indexer.Api.Tests.Authentication;

public sealed class KeycloakAuthenticationTests
{
    private static readonly KeycloakOptions Enabled = new()
    {
        Authority = KeycloakTestHost.Issuer,
    };

    private static readonly KeycloakOptions EnabledWithAudience = new()
    {
        Authority = KeycloakTestHost.Issuer,
        Audience = "ciir-indexer",
    };

    [Fact]
    public async Task KeycloakNotConfigured_RequestWithoutToken_IsAllowed()
    {
        using var host = await KeycloakTestHost.StartAsync(keycloak: null);

        var response = await host.GetTestClient().GetAsync(KeycloakTestHost.ProtectedEndpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeycloakNotConfigured_RequestWithAnUnvalidatedToken_IsStillAllowed()
    {
        using var host = await KeycloakTestHost.StartAsync(keycloak: null);
        var token = KeycloakTestHost.CreateToken(signingKey: KeycloakTestHost.NewKey('z'));

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeycloakConfigured_RequestWithoutToken_IsRejectedWithABearerChallengeAndProblemDetails()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);

        var response = await host.GetTestClient().GetAsync(KeycloakTestHost.ProtectedEndpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"status\":401");
        body.ShouldContain(KeycloakTestHost.ProtectedEndpoint);
    }

    [Fact]
    public async Task KeycloakConfigured_ValidToken_IsAllowed()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);

        var response = await host.GetTestClient().SendAsync(
            KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, KeycloakTestHost.CreateToken()));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeycloakConfigured_TokenSignedWithAnotherKey_IsRejected()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);
        var token = KeycloakTestHost.CreateToken(signingKey: KeycloakTestHost.NewKey('z'));

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KeycloakConfigured_ExpiredToken_IsRejected()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);
        var token = KeycloakTestHost.CreateToken(lifetime: TimeSpan.FromHours(-1));

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KeycloakConfigured_TokenFromAnotherIssuer_IsRejected()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);
        var token = KeycloakTestHost.CreateToken(issuer: "https://keycloak.test/realms/other");

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KeycloakConfiguredWithoutAudience_TokenForAnyAudience_IsAllowed()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);
        var token = KeycloakTestHost.CreateToken(audience: "account");

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task KeycloakConfiguredWithAudience_TokenWithThatAudience_IsAllowed()
    {
        using var host = await KeycloakTestHost.StartAsync(EnabledWithAudience);
        var token = KeycloakTestHost.CreateToken(audience: "ciir-indexer");

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("account")]
    public async Task KeycloakConfiguredWithAudience_TokenWithoutThatAudience_IsRejected(string? audience)
    {
        using var host = await KeycloakTestHost.StartAsync(EnabledWithAudience);
        var token = KeycloakTestHost.CreateToken(audience: audience);

        var response = await host.GetTestClient().SendAsync(KeycloakTestHost.Get(KeycloakTestHost.ProtectedEndpoint, token));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task KeycloakConfigured_EndpointOptedOutWithAllowAnonymous_IsAllowedWithoutToken()
    {
        using var host = await KeycloakTestHost.StartAsync(Enabled);

        var response = await host.GetTestClient().GetAsync(KeycloakTestHost.OpenEndpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
