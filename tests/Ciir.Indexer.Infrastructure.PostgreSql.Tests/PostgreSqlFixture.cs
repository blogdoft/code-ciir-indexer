using Ciir.Indexer.Infrastructure.PostgreSql.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

/// <summary>
/// One shared pgvector-enabled PostgreSQL container and migrated schema for the whole test
/// assembly (spec §60 - never substitute Postgres-specific behavior with an in-memory provider).
/// Each test uses a uniquely named project as its isolation boundary rather than a fresh
/// database, since spinning up a container per test class would be far slower.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const int EmbeddingDimensions = 4;

    private PostgreSqlContainer _container = null!;
    private ServiceProvider _serviceProvider = null!;

    public IServiceProvider Services => _serviceProvider;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddPostgreSqlPersistence(
            _container.GetConnectionString(),
            new IndexerDatabaseOptions { EmbeddingDimensions = EmbeddingDimensions });
        _serviceProvider = services.BuildServiceProvider();

        await _serviceProvider.GetRequiredService<DatabaseMigrator>().Execute();
    }

    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        await _container.DisposeAsync();
    }

    public NpgsqlDataSource GetDataSource() => _serviceProvider.GetRequiredService<NpgsqlDataSource>();
}
