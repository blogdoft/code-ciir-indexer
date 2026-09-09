namespace Ciir.Indexer.Core;

/// <summary>The lifecycle states of one indexing run (spec §26).</summary>
public enum IndexingStatus
{
    /// <summary>The run was created but has not started processing yet.</summary>
    Pending,

    /// <summary>Document Import and Relation Import are in progress.</summary>
    Running,

    /// <summary>Both imports finished; Relation Resolution is in progress.</summary>
    ResolvingRelations,

    /// <summary>The run finished successfully, including stale-entity cleanup.</summary>
    Completed,

    /// <summary>The run stopped due to an error; no valid prior data was touched.</summary>
    Failed,

    /// <summary>The run was cancelled before completion.</summary>
    Cancelled,
}
