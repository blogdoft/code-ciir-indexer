using FluentMigrator.Runner.Conventions;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.Options;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Migrations;

/// <summary>
/// Names FluentMigrator's own migration-history table "ciir-indexer-VersionInfo" instead of the
/// default "VersionInfo", so multiple applications sharing a database/schema each keep a
/// distinctly named history table. The unique index is renamed for the same reason: unlike the
/// column names below, an index name is a schema-wide identifier and would otherwise collide with
/// another application's default "UC_Version" index in the same schema.
/// </summary>
[VersionTableMetaData]
public sealed class CiirIndexerVersionTableMetaData : DefaultVersionTableMetaData
{
    public CiirIndexerVersionTableMetaData(IConventionSet conventionSet, IOptions<RunnerOptions> runnerOptions)
        : base(conventionSet, runnerOptions)
    {
    }

    public override string TableName => "ciir-indexer-VersionInfo";

    public override string UniqueIndexName => "UC_ciir-indexer-Version";
}
