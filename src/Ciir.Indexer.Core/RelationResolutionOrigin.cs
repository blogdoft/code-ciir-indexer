namespace Ciir.Indexer.Core;

/// <summary>
/// Where the CIIR generator located a relation's target. Preserved exactly as CIIR reported it.
/// </summary>
public enum RelationResolutionOrigin
{
    /// <summary>The target belongs to the analyzed project itself.</summary>
    Project,

    /// <summary>The target belongs to a different project within the same analyzed solution.</summary>
    Solution,

    /// <summary>The target belongs to a project dependency within analysis scope.</summary>
    Dependency,

    /// <summary>The target belongs to the base class library / platform framework.</summary>
    Framework,

    /// <summary>The target is only determinable at runtime.</summary>
    Runtime,

    /// <summary>The target belongs to an external service (e.g. a remote API contract).</summary>
    ExternalService,

    /// <summary>The origin could not be classified.</summary>
    Unknown,
}
