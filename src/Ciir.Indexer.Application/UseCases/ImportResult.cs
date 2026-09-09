using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// The outcome of one import pass (<see cref="ImportDocuments"/> or <see cref="ImportRelations"/>):
/// its counters, plus every project it touched - a single CIIR JSONL file can span many distinct
/// projects (spec §5's per-record "Resolve Project" step), so the orchestrator needs this set to
/// know which projects require relation resolution and stale-entity cleanup (spec §29).
/// </summary>
public sealed record ImportResult
{
    public required IndexingCounters Counters { get; init; }

    public required IReadOnlySet<long> ProjectIds { get; init; }
}
