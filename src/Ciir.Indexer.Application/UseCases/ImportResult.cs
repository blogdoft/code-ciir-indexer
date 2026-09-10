using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// The outcome of one import pass (<see cref="ImportDocuments"/> or <see cref="ImportRelations"/>):
/// its counters. The project every record is bound to is caller-supplied and already known to the
/// orchestrator (<see cref="RunIndexation"/>) before either import pass runs, so it is not part of
/// this result.
/// </summary>
public sealed record ImportResult
{
    public required IndexingCounters Counters { get; init; }
}
