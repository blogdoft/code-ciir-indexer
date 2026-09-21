using Ciir.Indexer.Api.Authentication;
using Ciir.Indexer.Api.OpenApi;
using Microsoft.OpenApi;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.OpenApi;

public sealed class KeycloakSecurityDocumentTransformerTests
{
    private const string Authority = "https://keycloak.example/realms/blogdoft";

    [Fact]
    public async Task TransformAsync_WithoutSwaggerClient_AddsOnlyTheBearerSchemeAndItsRequirement()
    {
        var document = await TransformAsync(new KeycloakOptions { Authority = Authority });

        var schemes = document.Components.ShouldNotBeNull().SecuritySchemes.ShouldNotBeNull();
        schemes.Keys.ShouldBe(["Bearer"]);
        var bearer = schemes["Bearer"].ShouldBeOfType<OpenApiSecurityScheme>();
        bearer.Type.ShouldBe(SecuritySchemeType.Http);
        bearer.Scheme.ShouldBe("bearer");
        bearer.BearerFormat.ShouldBe("JWT");
        var requirement = document.Security.ShouldNotBeNull().ShouldHaveSingleItem();
        requirement.Keys.ShouldHaveSingleItem().Reference.Id.ShouldBe("Bearer");
    }

    [Theory]
    [InlineData(Authority)]
    [InlineData(Authority + "/")]
    public async Task TransformAsync_WithSwaggerClient_AddsAKeycloakAuthorizationCodeSchemeDerivedFromTheAuthority(string authority)
    {
        var document = await TransformAsync(new KeycloakOptions { Authority = authority, ClientId = "swagger" });

        var schemes = document.Components.ShouldNotBeNull().SecuritySchemes.ShouldNotBeNull();
        schemes.Keys.ShouldBe(["Bearer", "OAuth2"], ignoreOrder: true);
        var oauth2 = schemes["OAuth2"].ShouldBeOfType<OpenApiSecurityScheme>();
        oauth2.Type.ShouldBe(SecuritySchemeType.OAuth2);
        var flow = oauth2.Flows.ShouldNotBeNull().AuthorizationCode.ShouldNotBeNull();
        flow.AuthorizationUrl.ShouldBe(new Uri($"{Authority}/protocol/openid-connect/auth"));
        flow.TokenUrl.ShouldBe(new Uri($"{Authority}/protocol/openid-connect/token"));
        flow.Scopes.ShouldNotBeNull().Keys.ShouldBe(["openid"]);
    }

    [Fact]
    public async Task TransformAsync_WithSwaggerClient_AcceptsEitherSchemeAsAlternativeRequirements()
    {
        var document = await TransformAsync(new KeycloakOptions { Authority = Authority, ClientId = "swagger" });

        var requirements = document.Security.ShouldNotBeNull();
        requirements.Count.ShouldBe(2);
        requirements.Select(r => r.Keys.Single().Reference.Id).ShouldBe(["Bearer", "OAuth2"], ignoreOrder: true);
        var oauth2Scopes = requirements.Single(r => r.Keys.Single().Reference.Id == "OAuth2").Values.Single();
        oauth2Scopes.ShouldBe(["openid"]);
    }

    private static async Task<OpenApiDocument> TransformAsync(KeycloakOptions options)
    {
        var document = new OpenApiDocument();
        await new KeycloakSecurityDocumentTransformer(options).TransformAsync(document, null!, CancellationToken.None);
        return document;
    }
}
