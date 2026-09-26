namespace Ciir.Indexer.Api.Authentication;

/// <summary>An access token issued by the realm through the <c>client_credentials</c> grant.</summary>
/// <param name="AccessToken">The access token (a JWT).</param>
/// <param name="TokenType">The token type reported by Keycloak (<c>Bearer</c>).</param>
/// <param name="ExpiresIn">The token's lifetime in seconds, when Keycloak reports it.</param>
public sealed record KeycloakToken(string AccessToken, string TokenType, int? ExpiresIn)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(KeycloakToken)} {{ TokenType = {TokenType}, ExpiresIn = {ExpiresIn} }}";
}
