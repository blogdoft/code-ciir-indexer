using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Api.Authentication;

/// <summary>Exchanges a client id and secret for an access token, so callers never need to know how the realm issues tokens.</summary>
public interface IKeycloakTokenClient
{
    /// <summary>Requests a token through the OAuth2 <c>client_credentials</c> grant.</summary>
    /// <param name="clientId">The realm client's id.</param>
    /// <param name="clientSecret">The realm client's secret.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// The issued token, or a failure whose code leads with the HTTP status to answer with:
    /// <c>401-invalid-client</c> when the realm refused the credentials, <c>502-token-endpoint-unavailable</c>
    /// when it could not be reached or answered with something unusable.
    /// </returns>
    Task<Result<KeycloakToken>> RequestTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken);
}
