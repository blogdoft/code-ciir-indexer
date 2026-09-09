namespace Ciir.Indexer.Core;

/// <summary>
/// CIIR's own classification of a relation target, preserved verbatim (spec §46/§47). An
/// unresolved <see cref="CiirRelation.TargetDocumentId"/> is only a defect when
/// <see cref="Status"/> is <see cref="RelationResolutionStatus.Resolved"/>.
/// </summary>
public sealed record CiirRelationResolution
{
    public required RelationResolutionStatus Status { get; init; }

    public required RelationResolutionOrigin Origin { get; init; }

    public string? Reason { get; init; }
}
