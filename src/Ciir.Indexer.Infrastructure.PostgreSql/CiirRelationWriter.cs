using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="ICiirRelationWriter" />
public sealed class CiirRelationWriter : ICiirRelationWriter
{
    private const string UpsertSql =
        """
        INSERT INTO ciir_relations (
            project_id, source_ciir_id, target_ciir_id, kind, target_symbol,
            resolution_status, resolution_origin, resolution_reason,
            source_path, start_line, start_column, end_line, end_column,
            idempotency_key, last_seen_run_id, updated_at
        ) VALUES (
            @ProjectId, @SourceCiirId, @TargetCiirId, @Kind, @TargetSymbol,
            @ResolutionStatus, @ResolutionOrigin, @ResolutionReason,
            @SourcePath, @StartLine, @StartColumn, @EndLine, @EndColumn,
            @IdempotencyKey, @RunId, now()
        )
        ON CONFLICT (idempotency_key) DO UPDATE SET
            resolution_status = EXCLUDED.resolution_status,
            resolution_origin = EXCLUDED.resolution_origin,
            resolution_reason = EXCLUDED.resolution_reason,
            last_seen_run_id = EXCLUDED.last_seen_run_id,
            updated_at = now();
        """;

    private const string DeleteStaleSql =
        """
        DELETE FROM ciir_relations
        WHERE project_id = @ProjectId AND (last_seen_run_id IS NULL OR last_seen_run_id != @CurrentRunId);
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IRelationIdentityKeyGenerator _identityKeyGenerator;

    public CiirRelationWriter(NpgsqlDataSource dataSource, IRelationIdentityKeyGenerator identityKeyGenerator)
    {
        _dataSource = dataSource;
        _identityKeyGenerator = identityKeyGenerator;
    }

    public async Task UpsertBatchAsync(
        IReadOnlyCollection<CiirRelation> batch,
        long projectId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var relation in batch)
            {
                await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                    new CommandDefinition(
                        UpsertSql, ToUpsertParameters(relation, projectId, runId), transaction, cancellationToken: cancellationToken)));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<long> DeleteStaleAsync(
        long projectId, Guid currentRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        return await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(
                DeleteStaleSql,
                new { ProjectId = projectId, CurrentRunId = currentRunId },
                cancellationToken: cancellationToken)));
    }

    private static string ToDb(RelationResolutionStatus status) => status switch
    {
        RelationResolutionStatus.Resolved => "resolved",
        RelationResolutionStatus.Unresolved => "unresolved",
        RelationResolutionStatus.Ambiguous => "ambiguous",
        RelationResolutionStatus.External => "external",
        RelationResolutionStatus.Dynamic => "dynamic",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };

    private static string ToDb(RelationResolutionOrigin origin) => origin switch
    {
        RelationResolutionOrigin.Project => "project",
        RelationResolutionOrigin.Solution => "solution",
        RelationResolutionOrigin.Dependency => "dependency",
        RelationResolutionOrigin.Framework => "framework",
        RelationResolutionOrigin.Runtime => "runtime",
        RelationResolutionOrigin.ExternalService => "external_service",
        RelationResolutionOrigin.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, message: null),
    };

    private object ToUpsertParameters(CiirRelation relation, long projectId, Guid runId) => new
    {
        ProjectId = projectId,
        SourceCiirId = relation.SourceCiirId.Value,
        TargetCiirId = relation.TargetCiirId?.Value,
        relation.Kind,
        relation.TargetSymbol,
        ResolutionStatus = ToDb(relation.Resolution.Status),
        ResolutionOrigin = ToDb(relation.Resolution.Origin),
        ResolutionReason = relation.Resolution.Reason,
        relation.SourcePath,
        relation.StartLine,
        relation.StartColumn,
        relation.EndLine,
        relation.EndColumn,
        IdempotencyKey = _identityKeyGenerator.Generate(projectId, relation),
        RunId = runId,
    };
}
