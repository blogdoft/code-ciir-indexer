namespace Ciir.Indexer.Api.Contracts;

/// <summary>Response body for a successful <c>POST /api/indexer/auth/token</c>.</summary>
/// <param name="AccessToken">The access token. Send it as <c>Authorization: Bearer &lt;accessToken&gt;</c>.</param>
/// <param name="TokenType">The token type; always <c>Bearer</c> in practice.</param>
/// <param name="ExpiresIn">The token's lifetime in seconds, when known.</param>
public sealed record TokenResponse(string AccessToken, string TokenType, int? ExpiresIn)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(TokenResponse)} {{ TokenType = {TokenType}, ExpiresIn = {ExpiresIn} }}";
}
