using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Authentication;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Controllers;

public sealed class AuthControllerTests
{
    private readonly IKeycloakTokenClient _tokenClient = Substitute.For<IKeycloakTokenClient>();

    [Theory]
    [InlineData(null, "secret")]
    [InlineData("", "secret")]
    [InlineData("   ", "secret")]
    [InlineData("client", null)]
    [InlineData("client", "")]
    [InlineData("client", "   ")]
    [InlineData(null, null)]
    public async Task CreateTokenAsync_MissingOrBlankCredential_ReturnsBadRequestWithoutCallingTheClient(string? clientId, string? clientSecret)
    {
        var result = await CreateSut(_tokenClient).CreateTokenAsync(new TokenRequest(clientId, clientSecret), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        objectResult.Value.ShouldBeOfType<ProblemDetails>().Instance.ShouldBe("/api/indexer/auth/token");
        await _tokenClient.DidNotReceive().RequestTokenAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTokenAsync_IssuedToken_ReturnsOkWithTheMappedResponse()
    {
        _tokenClient
            .RequestTokenAsync("ciir-pipeline", "secret", Arg.Any<CancellationToken>())
            .Returns(Result<KeycloakToken>.FromSuccess(new KeycloakToken("the-jwt", "Bearer", 300)));

        var result = await CreateSut(_tokenClient).CreateTokenAsync(new TokenRequest(" ciir-pipeline ", "secret"), CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        ok.Value.ShouldBe(new TokenResponse("the-jwt", "Bearer", 300));
    }

    [Theory]
    [InlineData("401-invalid-client", StatusCodes.Status401Unauthorized)]
    [InlineData("502-token-endpoint-unavailable", StatusCodes.Status502BadGateway)]
    public async Task CreateTokenAsync_FailedIssuance_ReturnsTheProblemAtTheEncodedStatus(string code, int expectedStatus)
    {
        _tokenClient
            .RequestTokenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<KeycloakToken>.FromFailure(new Failure(code, "some detail")));

        var result = await CreateSut(_tokenClient).CreateTokenAsync(new TokenRequest("client", "secret"), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(expectedStatus);
        objectResult.Value.ShouldBeOfType<ProblemDetails>().Detail.ShouldBe("some detail");
    }

    [Fact]
    public async Task CreateTokenAsync_AuthenticationTurnedOff_ReturnsBodylessNotFound()
    {
        var result = await CreateSut(tokenClient: null).CreateTokenAsync(new TokenRequest("client", "secret"), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    private static AuthController CreateSut(IKeycloakTokenClient? tokenClient)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/indexer/auth/token";

        return new AuthController(tokenClient)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }
}
