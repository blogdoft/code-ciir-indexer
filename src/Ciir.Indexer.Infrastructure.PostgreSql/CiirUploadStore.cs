using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="ICiirUploadStore" />
public sealed class CiirUploadStore : ICiirUploadStore
{
    // Dapper matches positional record constructor parameters to column names case-insensitively
    // but does NOT strip underscores the way it does for settable properties, so every column that
    // materializes into CiirUploadRow needs an explicit alias matching the parameter name exactly.
    private const string UploadColumns =
        """
        id AS "Id", project_id AS "ProjectId", bucket AS "Bucket", object_key AS "ObjectKey", status AS "Status",
        created_at AS "CreatedAt", processing_started_at AS "ProcessingStartedAt", processed_at AS "ProcessedAt",
        retry_count AS "RetryCount", error AS "Error", indexing_run_id AS "IndexingRunId"
        """;

    private const string CreateSql =
        $"""
        INSERT INTO ciir_uploads (id, project_id, bucket, object_key, status, created_at)
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
        RETURNING u.id AS "Id", u.project_id AS "ProjectId", u.bucket AS "Bucket", u.object_key AS "ObjectKey",
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
        UPDATE ciir_uploads SET indexing_run_id = @IndexingRunId WHERE id = @UploadId;
        """;

    private const string MarkProcessedSql =
        """
        UPDATE ciir_uploads SET status = 'processed', processed_at = now() WHERE id = @UploadId;
        """;

    private const string MarkFailedSql =
        """
        UPDATE ciir_uploads SET status = 'failed', processed_at = now(), error = @Error WHERE id = @UploadId;
        """;

    private const string GetSql =
        $"""
        SELECT {UploadColumns}
        FROM ciir_uploads WHERE id = @UploadId;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public CiirUploadStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CiirUpload> CreateAsync(
        long projectId, string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<CiirUploadRow>(
            new CommandDefinition(
                CreateSql,
                new
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Bucket = bucket,
                    ObjectKey = objectKey,
                    Status = CiirUploadStatus.Pending.ToWireString(),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken: cancellationToken)));

        return row.ToDomain();
    }

    public async Task<CiirUpload?> ClaimNextAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleOrDefaultAsync<CiirUploadRow?>(
            new CommandDefinition(
                ClaimNextSql,
                new { StuckTimeoutMinutes = stuckProcessingTimeout.TotalMinutes, MaxRetryCount = maxRetryCount },
                cancellationToken: cancellationToken)));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<CiirUpload>> ReclaimExhaustedAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var rows = await PostgreSqlConnections.ExecuteAsync(() => connection.QueryAsync<CiirUploadRow>(
            new CommandDefinition(
                ReclaimExhaustedSql,
                new { StuckTimeoutMinutes = stuckProcessingTimeout.TotalMinutes, MaxRetryCount = maxRetryCount },
                cancellationToken: cancellationToken)));

        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task MarkIndexingRunAsync(Guid uploadId, Guid indexingRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(
                MarkIndexingRunSql,
                new { UploadId = uploadId, IndexingRunId = indexingRunId },
                cancellationToken: cancellationToken)));
    }

    public async Task MarkProcessedAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(MarkProcessedSql, new { UploadId = uploadId }, cancellationToken: cancellationToken)));
    }

    public async Task MarkFailedAsync(Guid uploadId, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(
                MarkFailedSql, new { UploadId = uploadId, Error = error }, cancellationToken: cancellationToken)));
    }

    public async Task<CiirUpload?> GetAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleOrDefaultAsync<CiirUploadRow?>(
            new CommandDefinition(GetSql, new { UploadId = uploadId }, cancellationToken: cancellationToken)));

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
        public CiirUpload ToDomain() => new()
        {
            Id = Id,
            ProjectId = ProjectId,
            Bucket = Bucket,
            ObjectKey = ObjectKey,
            Status = CiirUploadStatusExtensions.ParseCiirUploadStatus(Status),
            CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
            ProcessingStartedAt = ProcessingStartedAt is { } processingStartedAt
                ? new DateTimeOffset(DateTime.SpecifyKind(processingStartedAt, DateTimeKind.Utc))
                : null,
            ProcessedAt = ProcessedAt is { } processedAt
                ? new DateTimeOffset(DateTime.SpecifyKind(processedAt, DateTimeKind.Utc))
                : null,
            RetryCount = RetryCount,
            Error = Error,
            IndexingRunId = IndexingRunId,
        };
    }
}
