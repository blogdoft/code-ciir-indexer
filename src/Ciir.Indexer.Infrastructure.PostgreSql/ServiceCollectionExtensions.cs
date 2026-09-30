using BlogDoFT.Libs.DapperUtils.Postgres;
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
        var dataSource = dataSourceBuilder.Build();
        services.AddSingleton(dataSource);
        services.AddDapperPostgres(new NpgsqlConnectionFactory(dataSource));

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

        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddScoped<ICiirDocumentWriter, CiirDocumentWriter>();
        services.AddScoped<ICiirRelationWriter, CiirRelationWriter>();
        services.AddScoped<IIndexingRunStore, IndexingRunStore>();
        services.AddScoped<ICiirUploadStore, CiirUploadStore>();
        services.AddScoped<IRelationResolver, RelationResolver>();
        services.AddSingleton<IRelationIdentityKeyGenerator, RelationIdentityKeyGenerator>();

        return services;
    }
}
