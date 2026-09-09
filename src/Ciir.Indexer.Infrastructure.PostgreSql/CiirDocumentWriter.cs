using Ciir.Indexer.Application.Ports;
using Dapper;
using Npgsql;
using Pgvector;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="ICiirDocumentWriter" />
public sealed class CiirDocumentWriter : ICiirDocumentWriter
{
    private const string GetExistingFingerprintsSql =
        """
        SELECT ciir_id, embedding_fingerprint_hash
        FROM ciir_documents
        WHERE project_id = @ProjectId AND ciir_id = ANY(@CiirIds);
        """;

    private const string UpsertSql =
        """
        INSERT INTO ciir_documents (
            project_id, ciir_id, schema_version, kind, language,
            symbol_name, symbol_qualified_name, symbol_canonical_name, symbol_container,
            source_path, embedding_text, embedding_text_strategy, embedding_text_hash,
            embedding_model, embedding_dimensions, embedding_fingerprint_hash,
            embedding, content, last_seen_run_id, updated_at
        ) VALUES (
            @ProjectId, @CiirId, @SchemaVersion, @Kind, @Language,
            @SymbolName, @SymbolQualifiedName, @SymbolCanonicalName, @SymbolContainer,
            @SourcePath, @EmbeddingText, @EmbeddingTextStrategy, @EmbeddingTextHash,
            @EmbeddingModel, @EmbeddingDimensions, @EmbeddingFingerprintHash,
            CASE WHEN @ReuseExistingEmbedding THEN NULL::vector ELSE @Embedding::vector END,
            @Content::jsonb, @RunId, now()
        )
        ON CONFLICT (project_id, ciir_id) DO UPDATE SET
            schema_version = EXCLUDED.schema_version,
            kind = EXCLUDED.kind,
            language = EXCLUDED.language,
            symbol_name = EXCLUDED.symbol_name,
            symbol_qualified_name = EXCLUDED.symbol_qualified_name,
            symbol_canonical_name = EXCLUDED.symbol_canonical_name,
            symbol_container = EXCLUDED.symbol_container,
            source_path = EXCLUDED.source_path,
            embedding_text = EXCLUDED.embedding_text,
            embedding_text_strategy = EXCLUDED.embedding_text_strategy,
            embedding_text_hash = EXCLUDED.embedding_text_hash,
            embedding_model = EXCLUDED.embedding_model,
            embedding_dimensions = EXCLUDED.embedding_dimensions,
            embedding_fingerprint_hash = EXCLUDED.embedding_fingerprint_hash,
            embedding = CASE WHEN @ReuseExistingEmbedding THEN ciir_documents.embedding ELSE EXCLUDED.embedding END,
            content = EXCLUDED.content,
            last_seen_run_id = EXCLUDED.last_seen_run_id,
            updated_at = now();
        """;

    private const string DeleteStaleSql =
        """
        DELETE FROM ciir_documents
        WHERE project_id = @ProjectId AND (last_seen_run_id IS NULL OR last_seen_run_id != @CurrentRunId);
        """;

    private readonly NpgsqlDataSource _dataSource;

    static CiirDocumentWriter()
    {
        VectorTypeHandler.Register();
    }

    public CiirDocumentWriter(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetExistingFingerprintsAsync(
        long projectId, IReadOnlyCollection<string> ciirIds, CancellationToken cancellationToken = default)
    {
        if (ciirIds.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var rows = await PostgreSqlConnections.ExecuteAsync(() => connection.QueryAsync<(string CiirId, string? EmbeddingFingerprintHash)>(
            new CommandDefinition(
                GetExistingFingerprintsSql,
                new { ProjectId = projectId, CiirIds = ciirIds.ToArray() },
                cancellationToken: cancellationToken)));

        return rows.ToDictionary(row => row.CiirId, row => row.EmbeddingFingerprintHash);
    }

    public async Task UpsertBatchAsync(
        IReadOnlyCollection<CiirDocumentUpsert> batch,
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
            foreach (var upsert in batch)
            {
                await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                    new CommandDefinition(
                        UpsertSql, ToUpsertParameters(upsert, projectId, runId), transaction, cancellationToken: cancellationToken)));
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

    private static object ToUpsertParameters(CiirDocumentUpsert upsert, long projectId, Guid runId)
    {
        var document = upsert.Document;

        return new
        {
            ProjectId = projectId,
            CiirId = document.CiirId.Value,
            document.SchemaVersion,
            document.Kind,
            document.Language,
            SymbolName = document.Symbol.Name,
            SymbolQualifiedName = document.Symbol.QualifiedName,
            SymbolCanonicalName = document.Symbol.CanonicalName,
            SymbolContainer = document.Symbol.Container,
            document.SourcePath,
            document.EmbeddingText,
            document.EmbeddingTextStrategy,
            EmbeddingTextHash = document.EmbeddingTextHash?.Value,
            upsert.EmbeddingModel,
            upsert.EmbeddingDimensions,
            upsert.EmbeddingFingerprintHash,
            Embedding = upsert.Embedding is { } vector ? new Vector(vector.ToArray()) : null,
            upsert.ReuseExistingEmbedding,
            Content = document.RawContent,
            RunId = runId,
        };
    }
}
