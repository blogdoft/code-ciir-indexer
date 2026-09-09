namespace Ciir.Indexer.Core;

/// <summary>
/// One directed, statically-observed CIIR relation (spec §15-25). Carries a dual identity: the
/// CIIR-native <see cref="SourceCiirId"/>/<see cref="TargetCiirId"/> (always present at import
/// time) and the resolved <see cref="SourceDocumentId"/>/<see cref="TargetDocumentId"/> internal
/// foreign keys (null until <c>Relation Resolution</c> runs, and possibly still null afterwards -
/// see <see cref="Resolution"/>). <see cref="Kind"/> and <see cref="TargetSymbol"/> come directly
/// from CIIR and are never reinterpreted by the indexer (spec §22).
/// </summary>
public sealed record CiirRelation
{
    public required CiirIdentity SourceCiirId { get; init; }

    public CiirIdentity? TargetCiirId { get; init; }

    public required string Kind { get; init; }

    public required string TargetSymbol { get; init; }

    public required CiirRelationResolution Resolution { get; init; }

    public string? SourcePath { get; init; }

    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }

    public long? SourceDocumentId { get; init; }

    public long? TargetDocumentId { get; init; }
}
