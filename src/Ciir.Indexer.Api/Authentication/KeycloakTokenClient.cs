using BlogDoFT.Libs.ResultPattern;
using System.Net;
using System.Text.Json;

namespace Ciir.Indexer.Api.Authentication;

/// <summary>
/// <see cref="IKeycloakTokenClient"/> over HTTP. The token endpoint is derived from
/// <see cref="KeycloakOptions.Authority"/> - the realm's public URL - and deliberately not from
/// <see cref="KeycloakOptions.MetadataAddress"/>: Keycloak computes a token's <c>iss</c> from the host
/// the request arrived on, and the JWT bearer handler validates <c>iss</c> against the Authority.
/// </summary>
internal sealed class KeycloakTokenClient(HttpClient httpClient, KeycloakOptions options, ILogger<KeycloakTokenClient> logger)
    : IKeycloakTokenClient
{
    private const string InvalidClientCode = "401-invalid-client";
    private const string UnavailableCode = "502-token-endpoint-unavailable";

    /// <inheritdoc />
    public async Task<Result<KeycloakToken>> RequestTokenAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        var tokenEndpoint = new Uri($"{options.Authority.TrimEnd('/')}/protocol/openid-connect/token");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("grant_type", "client_credentials"),
                    new("client_id", clientId),
                    new("client_secret", clientSecret),
                ]),
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            return await ReadResponseAsync(response, clientId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The Keycloak token endpoint could not be reached while issuing a token for client {ClientId}.", clientId);
            return Unavailable("The identity provider could not be reached.");
        }
    }

    private static Result<KeycloakToken> Unavailable(string message) =>
        Result<KeycloakToken>.FromFailure(new Failure(UnavailableCode, message));

    private static KeycloakToken? ParseToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out var accessToken)
                || accessToken.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(accessToken.GetString()))
            {
                return null;
            }

            var tokenType = root.TryGetProperty("token_type", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
            int? expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : null;

            return new KeycloakToken(accessToken.GetString()!, string.IsNullOrWhiteSpace(tokenType) ? "Bearer" : tokenType, expiresIn);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Result<KeycloakToken>> ReadResponseAsync(HttpResponseMessage response, string clientId, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Keycloak refused the credentials of client {ClientId} (HTTP {StatusCode}).", clientId, (int)response.StatusCode);
            return Result<KeycloakToken>.FromFailure(new Failure(InvalidClientCode, "The client credentials were rejected."));
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Keycloak answered HTTP {StatusCode} while issuing a token for client {ClientId}.", (int)response.StatusCode, clientId);
            return Unavailable("The identity provider could not issue a token.");
        }

        var token = ParseToken(await response.Content.ReadAsStringAsync(cancellationToken));
        if (token is null)
        {
            logger.LogWarning("Keycloak answered HTTP {StatusCode} without an access_token for client {ClientId}.", (int)response.StatusCode, clientId);
            return Unavailable("The identity provider answered with an unexpected response.");
        }

        logger.LogInformation("Issued a token for client {ClientId}.", clientId);
        return Result<KeycloakToken>.FromSuccess(token);
    }
}
