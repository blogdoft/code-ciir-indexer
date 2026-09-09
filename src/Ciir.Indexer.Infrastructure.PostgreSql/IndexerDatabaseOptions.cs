namespace Ciir.Indexer.Infrastructure.PostgreSql;

/// <summary>
/// Deployment-wide database configuration. <see cref="EmbeddingDimensions"/> bakes the
/// <c>vector(N)</c> column width at migration time - spec §10's v1 simplification of one fixed
/// embedding dimensionality per install, rather than per-model unconstrained vectors.
/// </summary>
public sealed class IndexerDatabaseOptions
{
    public required int EmbeddingDimensions { get; init; }
}
