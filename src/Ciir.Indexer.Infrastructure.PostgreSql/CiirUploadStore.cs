using BlogDoFT.Libs.DapperUtils.Abstractions;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="ICiirUploadStore" />
public sealed class CiirUploadStore : ICiirUploadStore
{
    // Dapper matches positional record constructor parameters to column names case-insensitively
    // but does NOT strip underscores the way it does for settable properties, so every column that
    // materializes into CiirUploadRow needs an explicit alias matching the parameter name exactly.
    // "id" (the numeric primary key) is deliberately never selected here - "public_id" is what
    // CiirUpload.Id (and every port/controller) actually works with.
    private const string UploadColumns =
        """
        public_id AS "Id", project_id AS "ProjectId", bucket AS "Bucket", object_key AS "ObjectKey", status AS "Status",
        created_at AS "CreatedAt", processing_started_at AS "ProcessingStartedAt", processed_at AS "ProcessedAt",
        retry_count AS "RetryCount", error AS "Error", indexing_run_id AS "IndexingRunId"
        """;

    private const string CreateSql =
        $"""
        INSERT INTO ciir_uploads (public_id, project_id, bucket, object_key, status, created_at)
        VALUES (@Id, @ProjectId, @Bucket, @ObjectKey, @Status, @CreatedAt)
        RETURNING {UploadColumns};
        """;

    // A single atomic statement (upload spec §8) so multiple worker instances can never claim the
    // same row: the CTE picks the oldest eligible row under FOR UPDATE SKIP LOCKED, and the UPDATE
    // transitions it to 'processing' in the same round trip. retry_count is only incremented when
    // the claimed row was already 'processing' (i.e. it was stuck, not freshly pending).
    private const string ClaimNextSql =
        """
        WITH next_upload AS (
            SELECT id
            FROM ciir_uploads
            WHERE status = 'pending'
               OR (
                    status = 'processing'
                    AND processing_started_at < now() - (@StuckTimeoutMinutes * interval '1 minute')
                    AND retry_count < @MaxRetryCount
                  )
            ORDER BY created_at ASC
            LIMIT 1
            FOR UPDATE SKIP LOCKED
        )
        UPDATE ciir_uploads u
        SET status = 'processing',
            processing_started_at = now(),
            retry_count = CASE WHEN u.status = 'processing' THEN u.retry_count + 1 ELSE u.retry_count END
        FROM next_upload
        WHERE u.id = next_upload.id
        RETURNING u.public_id AS "Id", u.project_id AS "ProjectId", u.bucket AS "Bucket", u.object_key AS "ObjectKey",
            u.status AS "Status", u.created_at AS "CreatedAt", u.processing_started_at AS "ProcessingStartedAt",
            u.processed_at AS "ProcessedAt", u.retry_count AS "RetryCount", u.error AS "Error",
            u.indexing_run_id AS "IndexingRunId";
        """;

    private const string ReclaimExhaustedSql =
        $"""
        UPDATE ciir_uploads
        SET status = 'failed',
            processed_at = now(),
            error = 'Exceeded the maximum retry count while stuck in processing.'
        WHERE status = 'processing'
          AND processing_started_at < now() - (@StuckTimeoutMinutes * interval '1 minute')
          AND retry_count >= @MaxRetryCount
        RETURNING {UploadColumns};
        """;

    private const string MarkIndexingRunSql =
        """
        UPDATE ciir_uploads SET indexing_run_id = @IndexingRunId WHERE public_id = @UploadId;
        """;

    private const string MarkProcessedSql =
        """
        UPDATE ciir_uploads SET status = 'processed', processed_at = now() WHERE public_id = @UploadId;
        """;

    private const string MarkFailedSql =
        """
        UPDATE ciir_uploads SET status = 'failed', processed_at = now(), error = @Error WHERE public_id = @UploadId;
        """;

    private const string GetSql =
        $"""
        SELECT {UploadColumns}
        FROM ciir_uploads WHERE public_id = @UploadId;
        """;

    private readonly IDatabaseFacade _database;

    public CiirUploadStore(IDatabaseFacade database)
    {
        _database = database;
    }

    public async Task<CiirUpload> CreateAsync(
        long projectId, string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var row = await _database.QuerySingleAsync<CiirUploadRow>(
            CreateSql,
            new
            {
                Id = Guid.CreateVersion7(),
                ProjectId = projectId,
                Bucket = bucket,
                ObjectKey = objectKey,
                Status = CiirUploadStatus.Pending.ToWireString(),
                CreatedAt = DateTimeOffset.UtcNow,
            });

        return row.ToDomain();
    }

    public async Task<CiirUpload?> ClaimNextAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default)
    {
        var row = await _database.QuerySingleOrDefaultAsync<CiirUploadRow?>(
            ClaimNextSql,
            new { StuckTimeoutMinutes = stuckProcessingTimeout.TotalMinutes, MaxRetryCount = maxRetryCount });

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<CiirUpload>> ReclaimExhaustedAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default)
    {
        var rows = await _database.QueryAsync<CiirUploadRow>(
            ReclaimExhaustedSql,
            new { StuckTimeoutMinutes = stuckProcessingTimeout.TotalMinutes, MaxRetryCount = maxRetryCount });

        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task MarkIndexingRunAsync(Guid uploadId, Guid indexingRunId, CancellationToken cancellationToken = default)
    {
        await _database.ExecuteAsync(MarkIndexingRunSql, new { UploadId = uploadId, IndexingRunId = indexingRunId });
    }

    public async Task MarkProcessedAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        await _database.ExecuteAsync(MarkProcessedSql, new { UploadId = uploadId });
    }

    public async Task MarkFailedAsync(Guid uploadId, string error, CancellationToken cancellationToken = default)
    {
        await _database.ExecuteAsync(MarkFailedSql, new { UploadId = uploadId, Error = error });
    }

    public async Task<CiirUpload?> GetAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var row = await _database.QuerySingleOrDefaultAsync<CiirUploadRow?>(GetSql, new { UploadId = uploadId });

        return row?.ToDomain();
    }

    // CreatedAt/ProcessingStartedAt/ProcessedAt are DateTime (not DateTimeOffset) here because
    // Npgsql's default CLR mapping for "timestamptz" is DateTime with Kind=Utc - ToDomain()
    // converts to the DateTimeOffset used by the Core CiirUpload domain type.
    private sealed record CiirUploadRow(
        Guid Id,
        long ProjectId,
        string Bucket,
        string ObjectKey,
        string Status,
        DateTime CreatedAt,
        DateTime? ProcessingStartedAt,
        DateTime? ProcessedAt,
        int RetryCount,
        string? Error,
        Guid? IndexingRunId)
    {
        public CiirUpload ToDomain()
        {
            var processingStartedAt = ProcessingStartedAt is { } startedAt
                ? new DateTimeOffset(DateTime.SpecifyKind(startedAt, DateTimeKind.Utc))
                : (DateTimeOffset?)null;
            var processedAt = ProcessedAt is { } finishedAt
                ? new DateTimeOffset(DateTime.SpecifyKind(finishedAt, DateTimeKind.Utc))
                : (DateTimeOffset?)null;

            return CiirUpload.Create(
                Id,
                ProjectId,
                Bucket,
                ObjectKey,
                CiirUploadStatusExtensions.ParseCiirUploadStatus(Status),
                new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
                processingStartedAt,
                processedAt,
                RetryCount,
                Error,
                IndexingRunId).Value;
        }
    }
}
