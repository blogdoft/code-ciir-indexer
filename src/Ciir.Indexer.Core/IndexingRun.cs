namespace Ciir.Indexer.Core;

/// <summary>
/// One indexation execution (spec §26). This is an operational entity, not part of the CIIR
/// contract itself - it exists to drive incremental indexing (<c>last_seen_run_id</c> staleness)
/// and to report progress back to the API caller.
/// </summary>
public sealed record IndexingRun
{
    public required Guid Id { get; init; }

    public required string Path { get; init; }

    public required long ProjectId { get; init; }

    public required IndexingStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public IndexingCounters Counters { get; init; } = new();

    public string? Error { get; init; }
}
