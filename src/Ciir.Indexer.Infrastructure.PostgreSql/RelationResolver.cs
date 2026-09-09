using Ciir.Indexer.Application.Ports;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="IRelationResolver" />
public sealed class RelationResolver : IRelationResolver
{
    private const string ResolveSourceSql =
        """
        UPDATE ciir_relations r
        SET source_document_id = d.id
        FROM ciir_documents d
        WHERE r.project_id = @ProjectId AND d.project_id = @ProjectId AND r.source_ciir_id = d.ciir_id;
        """;

    private const string ResolveTargetByCiirIdSql =
        """
        UPDATE ciir_relations r
        SET target_document_id = d.id
        FROM ciir_documents d
        WHERE r.project_id = @ProjectId
          AND d.project_id = @ProjectId
          AND r.target_ciir_id IS NOT NULL
          AND r.target_ciir_id = d.ciir_id;
        """;

    // Extension beyond spec §20's literal SQL, confirmed with the project owner: the C# CIIR
    // generator never populates relation.target.id in practice (see IRelationResolver's doc
    // comment), so the ciir_id match above resolves close to none of the internal edges on its
    // own. This pass falls back to matching target_symbol against symbol_canonical_name, scoped
    // to the project, only for rows CIIR itself classified as "resolved" and still unresolved
    // internally, and only when exactly one document matches. target_symbol is generator-populated
    // with the canonical form (includes parameter types), which already disambiguates ordinary
    // overloads - so match_count > 1 here means a genuine canonical-name collision (e.g. duplicate
    // documents, partial-class oddities), not routine overloading, and is left unresolved rather
    // than guessed. Also backfills target_ciir_id from the matched document's own ciir_id - CIIR
    // never supplied it, but once the match is made via symbol_canonical_name, the relation's own
    // CIIR-native identity (spec §17) is known and should be persisted alongside the FK, not left
    // inconsistent with a filled target_document_id.
    private const string ResolveTargetBySymbolSql =
        """
        WITH symbol_matches AS (
            SELECT
                d.id,
                d.ciir_id,
                d.symbol_canonical_name,
                COUNT(*) OVER (PARTITION BY d.symbol_canonical_name) AS match_count
            FROM ciir_documents d
            WHERE d.project_id = @ProjectId
        )
        UPDATE ciir_relations r
        SET target_document_id = m.id,
            target_ciir_id = m.ciir_id
        FROM symbol_matches m
        WHERE r.project_id = @ProjectId
          AND r.target_document_id IS NULL
          AND r.resolution_status = 'resolved'
          AND r.target_symbol = m.symbol_canonical_name
          AND m.match_count = 1;
        """;

    // CIIR's resolution_origin distinguishes "project" (target in the relation's own project) from
    // "solution" (target in a different project of the same analyzed solution) - a distinction that
    // only exists from CIIR schemaVersion 1.1 onward. The pass above can never satisfy a
    // solution-origin row (by definition its target document lives under a different project_id),
    // so this second, separate pass widens the document-side search to every project touched by the
    // current run - never touching "project"-origin rows, so it can only ever resolve additional
    // relations, never change behavior for what the pass above already handles.
    private const string ResolveTargetAcrossSolutionBySymbolSql =
        """
        WITH solution_symbol_matches AS (
            SELECT
                d.id,
                d.ciir_id,
                d.symbol_canonical_name,
                COUNT(*) OVER (PARTITION BY d.symbol_canonical_name) AS match_count
            FROM ciir_documents d
            WHERE d.project_id = ANY(@AllProjectIds)
        )
        UPDATE ciir_relations r
        SET target_document_id = m.id,
            target_ciir_id = m.ciir_id
        FROM solution_symbol_matches m
        WHERE r.project_id = @ProjectId
          AND r.target_document_id IS NULL
          AND r.resolution_status = 'resolved'
          AND r.resolution_origin = 'solution'
          AND r.target_symbol = m.symbol_canonical_name
          AND m.match_count = 1;
        """;

    // Column aliases match the RelationResolutionCounters property names exactly (PascalCase),
    // rather than relying on Dapper's underscore-stripping column-to-property matching.
    private const string CountersSql =
        """
        SELECT
            COUNT(*) AS "RelationsTotal",
            COUNT(*) FILTER (WHERE source_document_id IS NOT NULL) AS "SourceResolved",
            COUNT(*) FILTER (WHERE target_document_id IS NOT NULL) AS "TargetResolved",
            COUNT(*) FILTER (WHERE resolution_status = 'external') AS "ExternalTargets",
            COUNT(*) FILTER (WHERE resolution_status = 'unresolved') AS "UnresolvedTargets",
            COUNT(*) FILTER (WHERE resolution_status = 'ambiguous') AS "AmbiguousTargets",
            COUNT(*) FILTER (WHERE resolution_status = 'dynamic') AS "DynamicTargets"
        FROM ciir_relations
        WHERE project_id = @ProjectId;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public RelationResolver(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<RelationResolutionCounters> ResolveAsync(
        long projectId, IReadOnlyCollection<long> allProjectIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var parameters = new { ProjectId = projectId };
            var solutionParameters = new { ProjectId = projectId, AllProjectIds = allProjectIds.ToArray() };

            await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                new CommandDefinition(ResolveSourceSql, parameters, transaction, cancellationToken: cancellationToken)));
            await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                new CommandDefinition(ResolveTargetByCiirIdSql, parameters, transaction, cancellationToken: cancellationToken)));
            await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                new CommandDefinition(ResolveTargetBySymbolSql, parameters, transaction, cancellationToken: cancellationToken)));
            await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                new CommandDefinition(ResolveTargetAcrossSolutionBySymbolSql, solutionParameters, transaction, cancellationToken: cancellationToken)));

            var counters = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<RelationResolutionCounters>(
                new CommandDefinition(CountersSql, parameters, transaction, cancellationToken: cancellationToken)));

            await transaction.CommitAsync(cancellationToken);
            return counters;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
