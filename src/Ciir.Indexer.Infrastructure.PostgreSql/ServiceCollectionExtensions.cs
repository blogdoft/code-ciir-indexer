using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Ciir.Indexer.Infrastructure.PostgreSql.Migrations;
using Ciir.Indexer.Infrastructure.PostgreSql.Migrations.Migrations;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>Composition-root wiring for PostgreSQL persistence: data source, migrations, repositories.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services, string connectionString, IndexerDatabaseOptions databaseOptions)
    {
        services.AddSingleton(databaseOptions);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        services.AddSingleton(dataSourceBuilder.Build());

        // No .AddFluentMigratorConsole() here: that writes its own plain-text lines straight to the
        // console outside Microsoft.Extensions.Logging, which would bypass the app-wide JSON
        // formatter. FluentMigrator's own ILogger<T> messages already flow through whatever
        // providers the host has configured.
        services.AddFluentMigratorCore()
            .ConfigureRunner(runnerBuilder => runnerBuilder
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialSchema).Assembly).For.Migrations().For.VersionTableMetaData());
        services.AddSingleton<DatabaseMigrator>();

        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<ICiirDocumentWriter, CiirDocumentWriter>();
        services.AddSingleton<ICiirRelationWriter, CiirRelationWriter>();
        services.AddSingleton<IIndexingRunStore, IndexingRunStore>();
        services.AddSingleton<IRelationResolver, RelationResolver>();
        services.AddSingleton<IRelationIdentityKeyGenerator, RelationIdentityKeyGenerator>();

        return services;
    }
}
