using BlogDoFT.Libs.DapperUtils.Abstractions;
using BlogDoFT.Libs.DapperUtils.Postgres;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <inheritdoc cref="IProjectStore" />
public sealed class ProjectStore : IProjectStore
{
    private const string ResultSet =
        """
        SELECT id AS "Id", name AS "Name", git_url AS "GitUrl", git_raw_url AS "GitRawUrl",
            embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions",
            created_at AS "CreatedAt", updated_at AS "UpdatedAt"
        FROM projects
        """;

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
            embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions",
            created_at AS "CreatedAt", updated_at AS "UpdatedAt";
        """;

    private static readonly string GetByIdSql = $"{ResultSet} WHERE id = @Id;";

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

    public async Task<(IReadOnlyList<Project> Items, long TotalCount)> SearchAsync(
        string? nameFilter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // The interpolated fragments below are limited to a fixed, developer-controlled shape that
        // WhereBuilder/PaginatedSqlBuilder either includes verbatim or omits entirely - the
        // caller-supplied value still flows through the @NameFilter Dapper parameter, so this
        // isn't injectable.
#pragma warning disable S2077
        var (query, querySize) = new PaginatedSqlBuilder()
            .WithResultSet(ResultSet)
            .WithWhere(where => where.AndWith(nameFilter, "name ILIKE '%' || @NameFilter || '%'"))
            .MappingOrderWith("name", "name")
            .WithPagination(new PageFilter { Page = page, Size = pageSize, Order = "name" })
            .Build();
#pragma warning restore S2077

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var itemsCommand = new CommandDefinition(query.ToString(), new { NameFilter = nameFilter }, cancellationToken: cancellationToken);
        var countCommand = new CommandDefinition(querySize.ToString(), new { NameFilter = nameFilter }, cancellationToken: cancellationToken);

        var rows = await PostgreSqlConnections.ExecuteAsync(() => connection.QueryAsync<ProjectRow>(itemsCommand));
        var totalCount = await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteScalarAsync<long>(countCommand));

        return (rows.Select(r => r.ToDomain()).ToList(), totalCount);
    }

    public async Task<bool> ExistsByNameAsync(string name, long? excludingId, CancellationToken cancellationToken = default)
    {
#pragma warning disable S2077
        var where = new WhereBuilder()
            .AndWith(name, "name = @Name")
            .AndWith(excludingId, "id <> @ExcludingId")
            .Build();

        var sql = $"SELECT EXISTS (SELECT 1 FROM projects {where})";

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);
        var command = new CommandDefinition(sql, new { Name = name, ExcludingId = excludingId }, cancellationToken: cancellationToken);
#pragma warning restore S2077

        return await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteScalarAsync<bool>(command));
    }

    public async Task<Project> InsertAsync(
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default)
    {
        const string Sql =
            """
            INSERT INTO projects (name, git_url, git_raw_url, embedding_model, embedding_dimensions)
            VALUES (@Name, @GitUrl, @GitRawUrl, @EmbeddingModel, @EmbeddingDimensions)
            RETURNING id AS "Id", name AS "Name", git_url AS "GitUrl", git_raw_url AS "GitRawUrl",
                embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions",
                created_at AS "CreatedAt", updated_at AS "UpdatedAt";
            """;

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleAsync<ProjectRow>(
            new CommandDefinition(
                Sql,
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

    public async Task<Project?> UpdateAsync(
        long id,
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default)
    {
        const string Sql =
            """
            UPDATE projects
            SET name = @Name,
                git_url = @GitUrl,
                git_raw_url = @GitRawUrl,
                embedding_model = @EmbeddingModel,
                embedding_dimensions = @EmbeddingDimensions,
                updated_at = now()
            WHERE id = @Id
            RETURNING id AS "Id", name AS "Name", git_url AS "GitUrl", git_raw_url AS "GitRawUrl",
                embedding_model AS "EmbeddingModel", embedding_dimensions AS "EmbeddingDimensions",
                created_at AS "CreatedAt", updated_at AS "UpdatedAt";
            """;

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);

        var row = await PostgreSqlConnections.ExecuteAsync(() => connection.QuerySingleOrDefaultAsync<ProjectRow?>(
            new CommandDefinition(
                Sql,
                new
                {
                    Id = id,
                    Name = name,
                    GitUrl = gitUrl,
                    GitRawUrl = gitRawUrl,
                    EmbeddingModel = embeddingModel.Name,
                    EmbeddingDimensions = embeddingModel.Dimensions,
                },
                cancellationToken: cancellationToken)));

        return row?.ToDomain();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        const string Sql = "DELETE FROM projects WHERE id = @Id;";

        await using var connection = await PostgreSqlConnections.OpenAsync(_dataSource, cancellationToken);
        var affected = await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition(Sql, new { Id = id }, cancellationToken: cancellationToken)));

        return affected > 0;
    }

    private sealed record ProjectRow(
        long Id,
        string Name,
        string? GitUrl,
        string? GitRawUrl,
        string EmbeddingModel,
        int EmbeddingDimensions,
        DateTime CreatedAt,
        DateTime UpdatedAt)
    {
        // projects.created_at/updated_at are stored as timestamptz (always UTC); Npgsql returns
        // them with Kind=Unspecified, so it must be stamped explicitly to serialize with a "Z"
        // suffix.
        public Project ToDomain() => new()
        {
            Id = Id,
            Name = Name,
            GitUrl = GitUrl,
            GitRawUrl = GitRawUrl,
            EmbeddingModel = new EmbeddingModel(EmbeddingModel, EmbeddingDimensions),
            CreatedAt = DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            UpdatedAt = DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
        };
    }
}
