using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// Applies pending FluentMigrator migrations. FluentMigrator tracks what has already been applied
/// itself (its own VersionInfo table), so this is safe to call repeatedly. Resolves
/// <see cref="IMigrationRunner"/> from a fresh DI scope on every <see cref="Apply"/> call rather
/// than taking it as a constructor dependency, since FluentMigrator registers it as scoped
/// internally and this type is otherwise a singleton.
/// </summary>
public sealed class DatabaseMigrator
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseMigrator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Applies every migration newer than what has already run. Idempotent.</summary>
    public void Apply()
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
