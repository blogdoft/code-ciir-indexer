namespace Ciir.Indexer.Application.Ports;

/// <summary>Persists <see cref="Ciir.Indexer.Core.CiirDocument"/> nodes (spec §12).</summary>
public interface ICiirDocumentWriter
{
    /// <summary>
    /// Looks up the currently stored <c>embedding_fingerprint_hash</c> for each of the given
    /// <c>ciir_id</c> values within a project, in one set-based round trip (never one query per
    /// document). A <c>ciir_id</c> absent from the result, or mapped to a null value, means either
    /// the document does not exist yet or it has no stored embedding - both cases require
    /// generating an embedding when the incoming document has <c>embeddingText</c>.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="ciirIds">The <c>ciir_id</c> values to look up.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<IReadOnlyDictionary<string, string?>> GetExistingFingerprintsAsync(
        long projectId, IReadOnlyCollection<string> ciirIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a batch keyed by <c>(project_id, ciir_id)</c> (spec §12) in one small transaction
    /// (spec §30) and stamps <paramref name="runId"/> as <c>last_seen_run_id</c> on every row.
    /// </summary>
    /// <param name="batch">The documents to upsert, with their embedding decision already made.</param>
    /// <param name="projectId">The owning project.</param>
    /// <param name="runId">The current indexing run, stamped as <c>last_seen_run_id</c>.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task UpsertBatchAsync(
        IReadOnlyCollection<CiirDocumentUpsert> batch,
        long projectId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes documents in the project whose <c>last_seen_run_id</c> is not
    /// <paramref name="currentRunId"/> - only ever called after a fully successful run (spec §27).
    /// Returns the number of rows deleted.
    /// </summary>
    /// <param name="projectId">The owning project.</param>
    /// <param name="currentRunId">The run whose <c>last_seen_run_id</c> stamp is being kept.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<long> DeleteStaleAsync(long projectId, Guid currentRunId, CancellationToken cancellationToken = default);
}
