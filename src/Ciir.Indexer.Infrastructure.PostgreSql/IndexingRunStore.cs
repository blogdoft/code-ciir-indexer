using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="IIndexingRunStore" />
public sealed class IndexingRunStore : IIndexingRunStore
{
    // Dapper matches positional record constructor parameters to column names case-insensitively
    // but does NOT strip underscores the way it does for settable properties, so every column that
    // materializes into IndexingRunRow needs an explicit alias matching the parameter name exactly.
    // "id" (the numeric primary key) is deliberately never selected - "public_id" is what
    // IndexingRun.Id (and every port/controller) actually works with.
    private const string RunColumns =
        """
        public_id AS "Id", path AS "Path", project_id AS "ProjectId", status AS "Status", started_at AS "StartedAt",
        finished_at AS "FinishedAt",
        documents_processed AS "DocumentsProcessed", documents_inserted AS "DocumentsInserted",
        documents_updated AS "DocumentsUpdated", embeddings_generated AS "EmbeddingsGenerated",
        embeddings_reused AS "EmbeddingsReused", relations_processed AS "RelationsProcessed",
        relations_resolved AS "RelationsResolved", relations_unresolved AS "RelationsUnresolved", error AS "Error"
        """;

    private const string InsertSql =
        $"""
        INSERT INTO indexing_runs (public_id, path, project_id, status, started_at)
        VALUES (@Id, @Path, @ProjectId, @Status, @StartedAt)
        RETURNING {RunColumns};
        """;

    private const string UpdateCountersSql =
        """
        UPDATE indexing_runs SET
            documents_processed = @DocumentsProcessed,
            documents_inserted = @DocumentsInserted,
            documents_updated = @DocumentsUpdated,
            embeddings_generated = @EmbeddingsGenerated,
            embeddings_reused = @EmbeddingsReused,
            relations_processed = @RelationsProcessed,
            relations_resolved = @RelationsResolved,
            relations_unresolved = @RelationsUnresolved
        WHERE public_id = @RunId;
        """;

    private const string MarkStatusSql =
        """
        UPDATE indexing_runs SET
            status = @Status,
            error = @Error,
            finished_at = CASE WHEN @Status IN ('completed', 'failed', 'cancelled') THEN now() ELSE finished_at END
        WHERE public_id = @RunId;
        """;

    private const string GetSql =
        $"""
        SELECT {RunColumns}
        FROM indexing_runs WHERE public_id = @RunId;
        """;

    private const string ReconcileOrphanedRunsSql =
        """
        UPDATE indexing_runs SET
            status = 'failed',
            error = 'Process restarted while this run was in progress.',
            finished_at = now()
        WHERE status IN ('pending', 'running', 'resolving_relations')
        RETURNING public_id;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public IndexingRunStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IndexingRun> CreateAsync(string path, long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<IndexingRunRow>(
            new CommandDefinition(
                InsertSql,
                new
                {
                    Id = Guid.CreateVersion7(),
                    Path = path,
                    ProjectId = projectId,
                    Status = IndexingStatus.Pending.ToWireString(),
                    StartedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken: cancellationToken)));

        return row.ToDomain();
    }

    public async Task UpdateCountersAsync(
        Guid runId, IndexingCounters counters, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(
                UpdateCountersSql,
                new
                {
                    RunId = runId,
                    counters.DocumentsProcessed,
                    counters.DocumentsInserted,
                    counters.DocumentsUpdated,
                    counters.EmbeddingsGenerated,
                    counters.EmbeddingsReused,
                    counters.RelationsProcessed,
                    counters.RelationsResolved,
                    counters.RelationsUnresolved,
                },
                cancellationToken: cancellationToken)));
    }

    public async Task MarkStatusAsync(
        Guid runId, IndexingStatus status, string? error = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(
                MarkStatusSql,
                new { RunId = runId, Status = status.ToWireString(), Error = error },
                cancellationToken: cancellationToken)));
    }

    public async Task<IndexingRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleOrDefaultAsync<IndexingRunRow?>(
            new CommandDefinition(GetSql, new { RunId = runId }, cancellationToken: cancellationToken)));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyCollection<Guid>> ReconcileOrphanedRunsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var ids = await PostgreSqlConnections.ExecuteAsync(() => connection.QueryAsync<Guid>(
            new CommandDefinition(ReconcileOrphanedRunsSql, cancellationToken: cancellationToken)));

        return ids.ToList();
    }

    // StartedAt/FinishedAt are DateTime (not DateTimeOffset) here because Npgsql's default CLR
    // mapping for "timestamptz" is DateTime with Kind=Utc - ToDomain() converts to the
    // DateTimeOffset used by the Core IndexingRun domain type.
    private sealed record IndexingRunRow(
        Guid Id,
        string Path,
        long ProjectId,
        string Status,
        DateTime StartedAt,
        DateTime? FinishedAt,
        long DocumentsProcessed,
        long DocumentsInserted,
        long DocumentsUpdated,
        long EmbeddingsGenerated,
        long EmbeddingsReused,
        long RelationsProcessed,
        long RelationsResolved,
        long RelationsUnresolved,
        string? Error)
    {
        public IndexingRun ToDomain() => IndexingRun.Create(
            Id,
            Path,
            ProjectId,
            IndexingStatusExtensions.ParseIndexingStatus(Status),
            new DateTimeOffset(DateTime.SpecifyKind(StartedAt, DateTimeKind.Utc)),
            FinishedAt is { } finishedAt ? new DateTimeOffset(DateTime.SpecifyKind(finishedAt, DateTimeKind.Utc)) : null,
            new IndexingCounters
            {
                DocumentsProcessed = DocumentsProcessed,
                DocumentsInserted = DocumentsInserted,
                DocumentsUpdated = DocumentsUpdated,
                EmbeddingsGenerated = EmbeddingsGenerated,
                EmbeddingsReused = EmbeddingsReused,
                RelationsProcessed = RelationsProcessed,
                RelationsResolved = RelationsResolved,
                RelationsUnresolved = RelationsUnresolved,
            },
            Error).Value;
    }
}
