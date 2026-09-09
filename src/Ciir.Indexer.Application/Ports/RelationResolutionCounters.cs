namespace Ciir.Indexer.Application.Ports;

/// <summary>Resolution outcome metrics for one run (spec §47).</summary>
public sealed record RelationResolutionCounters
{
    public long RelationsTotal { get; init; }

    public long SourceResolved { get; init; }

    public long TargetResolved { get; init; }

    public long ExternalTargets { get; init; }

    public long UnresolvedTargets { get; init; }

    public long AmbiguousTargets { get; init; }

    public long DynamicTargets { get; init; }
}
