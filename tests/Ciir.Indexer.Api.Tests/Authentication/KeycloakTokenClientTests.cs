using Ciir.Indexer.Api.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Net;

namespace Ciir.Indexer.Api.Tests.Authentication;

public sealed class KeycloakTokenClientTests
{
    private const string Authority = "https://keycloak.test/realms/test";
    private const string ClientId = "ciir-pipeline";
    private const string ClientSecret = "s3cr3t-value/with+chars&more";
    private const string InvalidClientCode = "401-invalid-client";
    private const string UnavailableCode = "502-token-endpoint-unavailable";

    [Theory]
    [InlineData(Authority)]
    [InlineData(Authority + "/")]
    public async Task RequestTokenAsync_BuildsTheTokenEndpointFromTheAuthority(string authority)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, """{"access_token":"jwt"}""");

        await CreateSut(handler, authority).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        handler.RequestMethod.ShouldBe(HttpMethod.Post);
        handler.RequestUri.ShouldBe(new Uri($"{Authority}/protocol/openid-connect/token"));
    }

    [Fact]
    public async Task RequestTokenAsync_SendsOnlyTheClientCredentialsForm()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, """{"access_token":"jwt"}""");

        await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        handler.RequestContentType.ShouldBe("application/x-www-form-urlencoded");
        handler.RequestBody.ShouldBe(
            $"grant_type=client_credentials&client_id={Uri.EscapeDataString(ClientId)}&client_secret={Uri.EscapeDataString(ClientSecret)}");
    }

    [Fact]
    public async Task RequestTokenAsync_SuccessWithAllFields_ReturnsTheToken()
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK,
            """{"access_token":"the-jwt","token_type":"bearer","expires_in":300,"scope":"profile"}""");

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(new KeycloakToken("the-jwt", "bearer", 300));
    }

    [Fact]
    public async Task RequestTokenAsync_SuccessWithoutTokenTypeOrExpiry_DefaultsTheTypeToBearerAndLeavesExpiryUnset()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, """{"access_token":"the-jwt"}""");

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(new KeycloakToken("the-jwt", "Bearer", null));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RequestTokenAsync_RefusedCredentials_FailsWithInvalidClient(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Responding(status, """{"error":"unauthorized_client","error_description":"Invalid client secret"}""");

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(InvalidClientCode);
        result.Failure.Message.ShouldNotContain("Invalid client secret");
        result.Failure.Message.ShouldNotContain(ClientSecret);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task RequestTokenAsync_OtherErrorStatus_FailsAsUnavailable(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Responding(status, "boom");

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(UnavailableCode);
        result.Failure.Message.ShouldNotContain(ClientSecret);
    }

    [Fact]
    public async Task RequestTokenAsync_NetworkError_FailsAsUnavailable()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException($"connection refused ({ClientId})"));

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(UnavailableCode);
        result.Failure.Message.ShouldNotContain(ClientSecret);
    }

    [Fact]
    public async Task RequestTokenAsync_Timeout_FailsAsUnavailable()
    {
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("The request timed out."));

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(UnavailableCode);
        result.Failure.Message.ShouldNotContain(ClientSecret);
    }

    [Fact]
    public async Task RequestTokenAsync_CancelledByTheCaller_PropagatesTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("cancelled", null, cts.Token));

        await Should.ThrowAsync<TaskCanceledException>(
            () => CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, cts.Token));
    }

    [Theory]
    [InlineData("""{"token_type":"Bearer","expires_in":300}""")]
    [InlineData("""{"access_token":""}""")]
    [InlineData("""{"access_token":"   "}""")]
    [InlineData("""{"access_token":123}""")]
    [InlineData("""["access_token"]""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public async Task RequestTokenAsync_SuccessWithoutAUsableAccessToken_FailsAsUnavailable(string body)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, body);

        var result = await CreateSut(handler).RequestTokenAsync(ClientId, ClientSecret, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(UnavailableCode);
        result.Failure.Message.ShouldNotContain(ClientSecret);
    }

    private static KeycloakTokenClient CreateSut(StubHttpMessageHandler handler, string authority = Authority) =>
        new(
            new HttpClient(handler),
            new KeycloakOptions { Authority = authority },
            NullLogger<KeycloakTokenClient>.Instance);
}
