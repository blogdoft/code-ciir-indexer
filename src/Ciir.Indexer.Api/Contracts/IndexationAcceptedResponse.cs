namespace Ciir.Indexer.Api.Contracts;

/// <summary>Response body for a successful <c>POST /api/indexations</c> (spec §2).</summary>
/// <param name="IndexationId">
/// The newly created run's id. Pass this to <c>GET /api/indexations/{indexationId}</c> to poll for
/// progress and the final outcome.
/// </param>
/// <param name="Status">
/// The run's status immediately after creation. Always <c>"pending"</c> at this point - the
/// background worker has not necessarily started processing it yet.
/// </param>
public sealed record IndexationAcceptedResponse(Guid IndexationId, string Status);
