namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/indexer/auth/token</c>.</summary>
/// <param name="ClientId">Required. The realm client's id.</param>
/// <param name="ClientSecret">Required. The realm client's secret.</param>
public sealed record TokenRequest(string? ClientId, string? ClientSecret)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(TokenRequest)} {{ ClientId = {ClientId} }}";
}
