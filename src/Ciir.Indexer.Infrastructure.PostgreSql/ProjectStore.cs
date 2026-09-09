using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="IProjectStore" />
public sealed class ProjectStore : IProjectStore
{
    private const string UpsertSql =
        """
        INSERT INTO projects (name, embedding_model, embedding_dimensions)
        VALUES (@Name, @EmbeddingModel, @EmbeddingDimensions)
        ON CONFLICT (name) DO UPDATE SET
            embedding_model = EXCLUDED.embedding_model,
            embedding_dimensions = EXCLUDED.embedding_dimensions,
            updated_at = now()
        RETURNING id AS "Id", name AS "Name", embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions";
        """;

    private readonly NpgsqlDataSource _dataSource;

    public ProjectStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Project> EnsureProjectAsync(
        string name, EmbeddingModel embeddingModel, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<ProjectRow>(
            new CommandDefinition(
                UpsertSql,
                new { Name = name, EmbeddingModel = embeddingModel.Name, EmbeddingDimensions = embeddingModel.Dimensions },
                cancellationToken: cancellationToken)));

        return new Project
        {
            Id = row.Id,
            Name = row.Name,
            EmbeddingModel = new EmbeddingModel(row.EmbeddingModel, row.EmbeddingDimensions),
        };
    }

    private sealed record ProjectRow(long Id, string Name, string EmbeddingModel, int EmbeddingDimensions);
}
