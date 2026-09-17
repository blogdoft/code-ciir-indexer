namespace Ciir.Indexer.Api.Contracts;

/// <summary>Response body for <c>GET /api/ciir-uploads/{id}</c> (upload spec §4.1).</summary>
/// <param name="Id">The upload's id, matching the <c>uploadId</c> returned by <c>POST /api/ciir-uploads</c>.</param>
/// <param name="ProjectId">The already-registered project this file was submitted for.</param>
/// <param name="Status">
/// One of <c>"pending"</c>, <c>"processing"</c>, <c>"processed"</c>, or <c>"failed"</c>. Only
/// <c>"processed"</c> and <c>"failed"</c> are terminal - stop polling once you observe one of those.
/// </param>
/// <param name="CreatedAt">When the file was received and stored.</param>
/// <param name="ProcessingStartedAt">When the worker last claimed this upload; <c>null</c> while still <c>"pending"</c>.</param>
/// <param name="ProcessedAt">When processing reached a terminal outcome; <c>null</c> until then.</param>
/// <param name="IndexationId">
/// The <c>indexing_runs</c> id created for this upload once the worker starts processing it;
/// <c>null</c> while still <c>"pending"</c>. Poll <c>GET /api/indexations/{indexationId}</c> for
/// detailed indexation progress and counters.
/// </param>
/// <param name="Error">A human-readable failure message when <paramref name="Status"/> is <c>"failed"</c>; <c>null</c> otherwise.</param>
public sealed record CiirUploadStatusResponse(
    Guid Id,
    long ProjectId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessingStartedAt,
    DateTimeOffset? ProcessedAt,
    Guid? IndexationId,
    string? Error);
