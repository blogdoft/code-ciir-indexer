using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Authentication;
using Ciir.Indexer.Api.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using System.Net;
using System.Text;

namespace Ciir.Indexer.Api.Tests.Authentication;

public sealed class AuthControllerAnonymousAccessTests
{
    private const string TokenEndpoint = "/api/indexer/auth/token";

    private static readonly KeycloakOptions Enabled = new()
    {
        Authority = KeycloakTestHost.Issuer,
    };

    [Fact]
    public async Task KeycloakConfigured_TokenEndpointWithoutBearerToken_IsReachableAndIssuesAToken()
    {
        var tokenClient = Substitute.For<IKeycloakTokenClient>();
        tokenClient
            .RequestTokenAsync("ciir-pipeline", "secret", Arg.Any<CancellationToken>())
            .Returns(Result<KeycloakToken>.FromSuccess(new KeycloakToken("the-jwt", "Bearer", 300)));
        using var host = await StartHostAsync(tokenClient);

        var response = await host.GetTestClient().PostAsync(
            TokenEndpoint,
            new StringContent("""{"clientId":"ciir-pipeline","clientSecret":"secret"}""", Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"accessToken\":\"the-jwt\"");
        body.ShouldContain("\"tokenType\":\"Bearer\"");
        body.ShouldContain("\"expiresIn\":300");
    }

    [Fact]
    public async Task KeycloakConfigured_OtherEndpointWithoutAllowAnonymous_StillRequiresAToken()
    {
        using var host = await StartHostAsync(Substitute.For<IKeycloakTokenClient>());

        var response = await host.GetTestClient().GetAsync(KeycloakTestHost.ProtectedEndpoint);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Task<IHost> StartHostAsync(IKeycloakTokenClient tokenClient) =>
        KeycloakTestHost.StartAsync(
            Enabled,
            services =>
            {
                services.AddSingleton(tokenClient);
                services
                    .AddControllers()
                    .ConfigureApplicationPartManager(parts =>
                    {
                        parts.ApplicationParts.Clear();
                        parts.ApplicationParts.Add(new SingleControllerPart(typeof(AuthController)));
                    });
            },
            endpoints => endpoints.MapControllers());
}
