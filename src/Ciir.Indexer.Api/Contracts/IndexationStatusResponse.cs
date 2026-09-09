namespace Ciir.Indexer.Api.Contracts;

/// <summary>Response body for <c>GET /api/indexations/{id}</c> (spec §41).</summary>
/// <param name="Id">The indexation run's id, matching the <c>indexationId</c> returned by <c>POST /api/indexations</c>.</param>
/// <param name="Status">
/// One of <c>"pending"</c>, <c>"running"</c>, <c>"resolving_relations"</c>, <c>"completed"</c>,
/// <c>"failed"</c>, or <c>"cancelled"</c>. Only <c>"completed"</c>, <c>"failed"</c>, and
/// <c>"cancelled"</c> are terminal - stop polling once you observe one of those.
/// </param>
/// <param name="Documents">Document import progress and embedding reuse/regeneration counters.</param>
/// <param name="Relations">Relation import and resolution counters.</param>
/// <param name="Error">
/// A human-readable failure message when <paramref name="Status"/> is <c>"failed"</c>; <c>null</c>
/// for every other status.
/// </param>
public sealed record IndexationStatusResponse(
    Guid Id,
    string Status,
    IndexationDocumentsSummary Documents,
    IndexationRelationsSummary Relations,
    string? Error);
