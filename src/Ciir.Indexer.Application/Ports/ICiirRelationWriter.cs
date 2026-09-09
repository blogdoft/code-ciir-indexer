using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>Persists <see cref="CiirRelation"/> edges (spec §16), independently of documents.</summary>
public interface ICiirRelationWriter
{
    /// <summary>
    /// Upserts a batch keyed by the composite idempotency key from spec §25 (project +
    /// source_ciir_id + kind + target_ciir_id + target_symbol + location) in one small transaction
    /// and stamps <paramref name="runId"/> as <c>last_seen_run_id</c> on every row. Never requires
    /// the referenced documents to already exist (spec §18).
    /// </summary>
    /// <param name="batch">The relations to upsert.</param>
    /// <param name="projectId">The owning project.</param>
    /// <param name="runId">The current indexing run, stamped as <c>last_seen_run_id</c>.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task UpsertBatchAsync(
        IReadOnlyCollection<CiirRelation> batch,
        long projectId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes relations in the project whose <c>last_seen_run_id</c> is not
    /// <paramref name="currentRunId"/> - only ever called after a fully successful run, and before
    /// <see cref="ICiirDocumentWriter.DeleteStaleAsync"/> per the FK deletion order (spec §29).
    /// Returns the number of rows deleted.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="currentRunId">The run whose <c>last_seen_run_id</c> stamp is being kept.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<long> DeleteStaleAsync(long projectId, Guid currentRunId, CancellationToken cancellationToken = default);
}
