using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Persists <see cref="CiirUpload"/> lifecycle state (upload spec §6/§7/§8) - the durable queue a
/// polling <c>BackgroundService</c> claims work from, deliberately separate from
/// <see cref="IIndexingRunStore"/> (which tracks the indexation itself, not the upload/blob
/// lifecycle).
/// </summary>
public interface ICiirUploadStore
{
    /// <summary>Creates a new upload row in <see cref="CiirUploadStatus.Pending"/>.</summary>
    /// <param name="projectId">The already-registered project this file will be indexed into.</param>
    /// <param name="bucket">The MinIO bucket the file was stored in.</param>
    /// <param name="objectKey">The MinIO object key the file was stored under.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<CiirUpload> CreateAsync(
        long projectId, string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the oldest eligible upload - one that is <see cref="CiirUploadStatus.Pending"/>,
    /// or <see cref="CiirUploadStatus.Processing"/> for longer than <paramref name="stuckProcessingTimeout"/>
    /// and has not yet exhausted <paramref name="maxRetryCount"/> - transitioning it to
    /// <see cref="CiirUploadStatus.Processing"/> in the same statement (<c>SELECT ... FOR UPDATE SKIP
    /// LOCKED</c>, upload spec §8) so multiple worker instances never claim the same row.
    /// </summary>
    /// <param name="stuckProcessingTimeout">How long a row may stay in <see cref="CiirUploadStatus.Processing"/> before being considered abandoned.</param>
    /// <param name="maxRetryCount">The maximum number of times a stuck row may be re-claimed.</param>
    /// <param name="cancellationToken">Propagates shutdown cancellation.</param>
    /// <returns>The claimed upload, or <c>null</c> if nothing was eligible.</returns>
    Task<CiirUpload?> ClaimNextAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks as permanently <see cref="CiirUploadStatus.Failed"/> every upload stuck in
    /// <see cref="CiirUploadStatus.Processing"/> past <paramref name="stuckProcessingTimeout"/> that
    /// has already exhausted <paramref name="maxRetryCount"/> - these are never claimed again
    /// (upload spec §8, step 1). Returned so the caller can also delete their now-orphaned MinIO
    /// objects.
    /// </summary>
    /// <param name="stuckProcessingTimeout">How long a row may stay in <see cref="CiirUploadStatus.Processing"/> before being considered abandoned.</param>
    /// <param name="maxRetryCount">The retry count at or above which a stuck row is failed permanently instead of re-claimed.</param>
    /// <param name="cancellationToken">Propagates shutdown cancellation.</param>
    Task<IReadOnlyList<CiirUpload>> ReclaimExhaustedAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default);

    /// <summary>Records which <see cref="IndexingRun"/> was created for this upload.</summary>
    /// <param name="uploadId">The upload to update.</param>
    /// <param name="indexingRunId">The created run's id.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task MarkIndexingRunAsync(Guid uploadId, Guid indexingRunId, CancellationToken cancellationToken = default);

    /// <summary>Marks an upload as successfully processed (upload spec §8, step 4.i).</summary>
    /// <param name="uploadId">The upload to update.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task MarkProcessedAsync(Guid uploadId, CancellationToken cancellationToken = default);

    /// <summary>Marks an upload as permanently failed - it will not be retried (upload spec §7).</summary>
    /// <param name="uploadId">The upload to update.</param>
    /// <param name="error">A human-readable description of why processing stopped.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task MarkFailedAsync(Guid uploadId, string error, CancellationToken cancellationToken = default);

    /// <summary>Reads back one upload's current state, for <c>GET /api/ciir-uploads/{id}</c>.</summary>
    /// <param name="uploadId">The upload to look up.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<CiirUpload?> GetAsync(Guid uploadId, CancellationToken cancellationToken = default);
}
