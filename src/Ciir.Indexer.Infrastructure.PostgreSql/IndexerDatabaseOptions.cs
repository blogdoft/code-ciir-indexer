namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>
/// Deployment-wide database configuration. <see cref="EmbeddingDimensions"/> bakes the
/// <c>vector(N)</c> column width at migration time - spec §10's v1 simplification of one fixed
/// embedding dimensionality per install, rather than per-model unconstrained vectors.
/// </summary>
public sealed class IndexerDatabaseOptions
{
    public required int EmbeddingDimensions { get; init; }

    /// <summary>
    /// The connection string <see cref="Migrations.DatabaseMigrator"/> runs migrations against.
    /// Kept here (rather than injected as a bare <see cref="string"/>) so DatabaseMigrator can use
    /// plain constructor injection and be picked up automatically as an
    /// <c>IWarmUpCommand</c> - a service registered via a factory delegate has no
    /// <c>ImplementationType</c> for that discovery to find.
    /// </summary>
    public required string ConnectionString { get; init; }
}
