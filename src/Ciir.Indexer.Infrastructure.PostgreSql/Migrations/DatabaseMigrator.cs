using BlogDoFT.Libs.WarmUp;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// Applies pending FluentMigrator migrations. FluentMigrator tracks what has already been applied
/// itself (its own VersionInfo table), so this is safe to call repeatedly. Runs as an
/// <see cref="IWarmUpCommand"/> (<c>BlogDoFT.Libs.WarmUp</c>) rather than blocking the host
/// synchronously before it starts accepting connections: the process comes up and starts serving
/// immediately, and <c>WarmUpHealthCheck</c> reports "unhealthy" on <c>/health</c> until this
/// finishes, so Kubernetes withholds traffic until migrations are actually done. A Postgres
/// advisory lock serializes multiple replicas that start this warm-up concurrently, since
/// FluentMigrator's own VersionInfo table does not by itself prevent two instances from racing to
/// apply the same migration at boot.
/// </summary>
public sealed class DatabaseMigrator : IWarmUpCommand
{
    // Advisory locks share one keyspace per Postgres database/instance (see
    // CiirIndexerVersionTableMetaData for the same "other apps may share this schema" concern), so
    // the key is derived from an app-specific string rather than a small literal that could collide
    // with another application's lock.
    private static readonly long AdvisoryLockKey = BitConverter.ToInt64(
        SHA256.HashData(Encoding.UTF8.GetBytes("ciir-indexer-migrations")), 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _connectionString;

    public DatabaseMigrator(IServiceScopeFactory scopeFactory, IndexerDatabaseOptions databaseOptions)
    {
        _scopeFactory = scopeFactory;
        _connectionString = databaseOptions.ConnectionString;
    }

    /// <summary>Applies every migration newer than what has already run, under an advisory lock. Idempotent.</summary>
    public async Task Execute()
    {
        // Deliberately a bare NpgsqlConnection over this string, not the app-wide pgvector-enabled
        // NpgsqlDataSource every repository shares: that data source's "vector" type mapping is
        // resolved from pg_type on first use and must not be primed before CREATE EXTENSION vector
        // (run by the migrations below) has actually created the type.
        await using var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException ex)
        {
            throw new DatabaseUnavailableException($"Could not connect to PostgreSQL: {ex.Message}", ex);
        }

        // pg_advisory_lock is session-scoped: it must be released on the same connection that
        // acquired it, so the lock/migrate/unlock sequence below stays on this one connection.
        await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
            new CommandDefinition("SELECT pg_advisory_lock(@Key);", new { Key = AdvisoryLockKey })));
        try
        {
            Apply();
        }
        finally
        {
            await PostgreSqlConnections.ExecuteAsync(() => connection.ExecuteAsync(
                new CommandDefinition("SELECT pg_advisory_unlock(@Key);", new { Key = AdvisoryLockKey })));
        }
    }

    private void Apply()
    {
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        try
        {
            runner.MigrateUp();
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException)
        {
            throw new DatabaseUnavailableException($"Could not connect to PostgreSQL: {ex.Message}", ex);
        }
    }
}
