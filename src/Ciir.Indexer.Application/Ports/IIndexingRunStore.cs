using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>Persists <see cref="IndexingRun"/> execution state (spec §26/§27).</summary>
public interface IIndexingRunStore
{
    Task<IndexingRun> CreateAsync(string path, long projectId, CancellationToken cancellationToken = default);

    Task UpdateCountersAsync(Guid runId, IndexingCounters counters, CancellationToken cancellationToken = default);

    Task MarkStatusAsync(
        Guid runId, IndexingStatus status, string? error = null, CancellationToken cancellationToken = default);

    Task<IndexingRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks any run left in <see cref="IndexingStatus.Pending"/>, <see cref="IndexingStatus.Running"/>
    /// or <see cref="IndexingStatus.ResolvingRelations"/> from a previous process lifetime as
    /// <see cref="IndexingStatus.Failed"/> - a pure status update, never a data deletion, run once
    /// at process startup to recover from a crash mid-run (spec §27's "never clean up on partial
    /// failure" extends naturally to this: it only marks status).
    /// </summary>
    /// <param name="cancellationToken">Propagates shutdown cancellation.</param>
    Task<IReadOnlyCollection<Guid>> ReconcileOrphanedRunsAsync(CancellationToken cancellationToken = default);
}
