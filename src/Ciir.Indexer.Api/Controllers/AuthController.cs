using Ciir.Indexer.Api.Authentication;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Problems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Controllers;

/// <summary>
/// Token gateway: lets a non-interactive client (e.g. a CI pipeline running <c>ciir --send</c>)
/// exchange its Keycloak client id and secret for an access token by talking only to this service,
/// without knowing where or how the realm issues tokens. Contains no authentication logic of its
/// own - it validates the request shape and delegates to <see cref="IKeycloakTokenClient"/>.
/// </summary>
[ApiController]
[Route("api/indexer/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IKeycloakTokenClient? _tokenClient;

    /// <summary>Initializes a new instance of the <see cref="AuthController"/> class.</summary>
    /// <param name="tokenClient">Issues the token; <see langword="null"/> when authentication is turned off, since there is then no realm to issue one.</param>
    public AuthController(IKeycloakTokenClient? tokenClient = null)
    {
        _tokenClient = tokenClient;
    }

    /// <summary>Exchanges a client id and secret for an access token (OAuth2 <c>client_credentials</c>).</summary>
    /// <remarks>
    /// Anonymous by necessity: it is what produces the token the other endpoints require. Only the
    /// <c>client_credentials</c> grant is supported; the request cannot choose a grant or scope. Send
    /// the returned <c>accessToken</c> as <c>Authorization: Bearer &lt;accessToken&gt;</c>. Use HTTPS:
    /// the request body carries the client secret.
    /// </remarks>
    /// <param name="request">The client id and secret.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="TokenResponse"/>; <c>400</c> Problem Details when
    /// <c>clientId</c>/<c>clientSecret</c> is missing; <c>401</c> Problem Details when the realm
    /// refuses the credentials; <c>502</c> Problem Details when the realm cannot be reached or answers
    /// with something unusable; a body-less <c>404</c> when authentication is turned off.
    /// </returns>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    public async Task<IActionResult> CreateTokenAsync([FromBody] TokenRequest request, CancellationToken cancellationToken)
    {
        if (_tokenClient is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return ProblemResults.Build(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "'clientId' and 'clientSecret' are required.",
                Request.Path);
        }

        var result = await _tokenClient.RequestTokenAsync(request.ClientId.Trim(), request.ClientSecret, cancellationToken);

        return result.IsFailure
            ? result.Failure.ToActionResult(HttpContext)
            : Ok(new TokenResponse(result.Value.AccessToken, result.Value.TokenType, result.Value.ExpiresIn));
    }
}
