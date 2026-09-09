namespace Ciir.Indexer.Core;

/// <summary>
/// How the CIIR generator classified a relation's target. Preserved exactly as CIIR reported it -
/// the indexer never reinterprets or improves on this classification (spec §46).
/// </summary>
public enum RelationResolutionStatus
{
    /// <summary>The target was located; it may or may not resolve to a document in this project.</summary>
    Resolved,

    /// <summary>The target should exist within analysis scope but could not be located.</summary>
    Unresolved,

    /// <summary>Multiple candidate targets were found and none could be preferred.</summary>
    Ambiguous,

    /// <summary>The target lives outside analysis scope (e.g. a framework/dependency member).</summary>
    External,

    /// <summary>The target can only be determined at runtime (e.g. reflection, dynamic dispatch).</summary>
    Dynamic,
}
