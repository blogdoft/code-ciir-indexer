namespace Ciir.Indexer.Core;

/// <summary>The observability counters tracked for one indexing run (spec §26/§41/§47).</summary>
public sealed record IndexingCounters
{
    public long DocumentsProcessed { get; init; }

    public long DocumentsInserted { get; init; }

    public long DocumentsUpdated { get; init; }

    public long EmbeddingsGenerated { get; init; }

    public long EmbeddingsReused { get; init; }

    public long RelationsProcessed { get; init; }

    public long RelationsResolved { get; init; }

    public long RelationsUnresolved { get; init; }
}
