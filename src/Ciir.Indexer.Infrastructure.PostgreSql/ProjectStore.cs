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
        INSERT INTO projects (name, git_url, git_raw_url, embedding_model, embedding_dimensions)
        VALUES (@Name, @GitUrl, @GitRawUrl, @EmbeddingModel, @EmbeddingDimensions)
        ON CONFLICT (name) DO UPDATE SET
            git_url = EXCLUDED.git_url,
            git_raw_url = EXCLUDED.git_raw_url,
            embedding_model = EXCLUDED.embedding_model,
            embedding_dimensions = EXCLUDED.embedding_dimensions,
            updated_at = now()
        RETURNING id AS "Id", name AS "Name", git_url AS "GitUrl", git_raw_url AS "GitRawUrl",
            embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions";
        """;

    private const string GetByIdSql =
        """
        SELECT id AS "Id", name AS "Name", git_url AS "GitUrl", git_raw_url AS "GitRawUrl",
            embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions"
        FROM projects WHERE id = @Id;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public ProjectStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<Project?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleOrDefaultAsync<ProjectRow?>(
            new CommandDefinition(GetByIdSql, new { Id = id }, cancellationToken: cancellationToken)));

        return row?.ToDomain();
    }

    public async Task<Project> EnsureProjectAsync(
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<ProjectRow>(
            new CommandDefinition(
                UpsertSql,
                new
                {
                    Name = name,
                    GitUrl = gitUrl,
                    GitRawUrl = gitRawUrl,
                    EmbeddingModel = embeddingModel.Name,
                    EmbeddingDimensions = embeddingModel.Dimensions,
                },
                cancellationToken: cancellationToken)));

        return row.ToDomain();
    }

    private sealed record ProjectRow(
        long Id, string Name, string? GitUrl, string? GitRawUrl, string EmbeddingModel, int EmbeddingDimensions)
    {
        public Project ToDomain() => new()
        {
            Id = Id,
            Name = Name,
            GitUrl = GitUrl,
            GitRawUrl = GitRawUrl,
            EmbeddingModel = new EmbeddingModel(EmbeddingModel, EmbeddingDimensions),
        };
    }
}
