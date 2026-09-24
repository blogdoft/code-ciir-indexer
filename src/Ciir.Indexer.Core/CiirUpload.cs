using BlogDoFT.Libs.ResultPattern;
using FluentValidation;

namespace Ciir.Indexer.Core;

/// <summary>
/// One CIIR file received via <c>POST /api/ciir-uploads</c> (upload spec §6). This is a queue/blob
/// lifecycle entity, separate from <see cref="IndexingRun"/>: it tracks whether the file is sitting
/// in MinIO, being processed, or done - the actual indexation progress/counters live on the
/// <see cref="IndexingRun"/> it eventually creates (<see cref="IndexingRunId"/>).
/// </summary>
public sealed record CiirUpload
{
    private static readonly CiirUploadValidator Validator = new();

    private CiirUpload()
    {
    }

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

    /// <summary>Validates and creates a <see cref="CiirUpload"/>.</summary>
    /// <param name="id">The upload's identity.</param>
    /// <param name="projectId">The already-registered project this file will be indexed into.</param>
    /// <param name="bucket">The MinIO bucket the file was stored in.</param>
    /// <param name="objectKey">The MinIO object key the file was stored under.</param>
    /// <param name="status">The upload's current lifecycle state.</param>
    /// <param name="createdAt">When the upload row was created.</param>
    /// <param name="processingStartedAt">When the worker last claimed this upload, if any.</param>
    /// <param name="processedAt">When processing finished (successfully or not), if any.</param>
    /// <param name="retryCount">How many times a stuck upload has been re-claimed.</param>
    /// <param name="error">A human-readable description of why processing stopped, if it did.</param>
    /// <param name="indexingRunId">The <see cref="IndexingRun"/> created for this upload, if any.</param>
    public static Result<CiirUpload> Create(
        Guid id,
        long projectId,
        string? bucket,
        string? objectKey,
        CiirUploadStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? processingStartedAt = null,
        DateTimeOffset? processedAt = null,
        int retryCount = 0,
        string? error = null,
        Guid? indexingRunId = null)
    {
        var candidate = new CiirUpload
        {
            Id = id,
            ProjectId = projectId,
            Bucket = bucket ?? string.Empty,
            ObjectKey = objectKey ?? string.Empty,
            Status = status,
            CreatedAt = createdAt,
            ProcessingStartedAt = processingStartedAt,
            ProcessedAt = processedAt,
            RetryCount = retryCount,
            Error = error,
            IndexingRunId = indexingRunId,
        };

        var validation = Validator.Validate(candidate);
        if (!validation.IsValid)
        {
            return Result<CiirUpload>.FromFailure(new Failure("400-ciir-upload-invalid", validation.Errors[0].ErrorMessage));
        }

        return Result<CiirUpload>.FromSuccess(candidate);
    }

    private sealed class CiirUploadValidator : AbstractValidator<CiirUpload>
    {
        public CiirUploadValidator()
        {
            RuleFor(upload => upload.ProjectId).GreaterThan(0);
            RuleFor(upload => upload.Bucket).NotEmpty();
            RuleFor(upload => upload.ObjectKey).NotEmpty();
            RuleFor(upload => upload.RetryCount).GreaterThanOrEqualTo(0);
        }
    }
}
