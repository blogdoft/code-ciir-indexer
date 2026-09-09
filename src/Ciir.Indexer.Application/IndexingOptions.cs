namespace Ciir.Indexer.Application;

/// <summary>
/// Orchestration-level configuration for the import use cases - deliberately separate from the
/// embedding provider's own batch size (spec §30/§31: DB write batches and embedding-call batches
/// are independent concerns and are typically sized very differently, e.g. hundreds of rows per DB
/// transaction vs. dozens of texts per embedding HTTP call).
/// </summary>
public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>Maximum documents upserted per database transaction (spec §30).</summary>
    public int DocumentBatchSize { get; set; } = 500;

    /// <summary>Maximum relations upserted per database transaction (spec §30).</summary>
    public int RelationBatchSize { get; set; } = 500;
}
