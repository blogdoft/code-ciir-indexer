namespace Ciir.Indexer.Core;

/// <summary>
/// One CIIR file received via <c>POST /api/ciir-uploads</c> (upload spec §6). This is a queue/blob
/// lifecycle entity, separate from <see cref="IndexingRun"/>: it tracks whether the file is sitting
/// in MinIO, being processed, or done - the actual indexation progress/counters live on the
/// <see cref="IndexingRun"/> it eventually creates (<see cref="IndexingRunId"/>).
/// </summary>
public sealed record CiirUpload
{
    public required Guid Id { get; init; }

    /// <summary>The already-registered project this file will be indexed into (upload spec §3).</summary>
    public required long ProjectId { get; init; }

    public required string Bucket { get; init; }

    public required string ObjectKey { get; init; }

    public required CiirUploadStatus Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ProcessingStartedAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; init; }

    /// <summary>
    /// How many times the worker has re-claimed this upload after finding it stuck in
    /// <see cref="CiirUploadStatus.Processing"/> past the configured timeout (upload spec §7) -
    /// never incremented for a deterministic indexation failure, only for a worker crash mid-run.
    /// </summary>
    public int RetryCount { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// The <see cref="IndexingRun"/> created for this upload once the worker starts processing it;
    /// <c>null</c> while the upload is still <see cref="CiirUploadStatus.Pending"/>.
    /// </summary>
    public Guid? IndexingRunId { get; init; }
}
