using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Relation Resolution (spec §20): runs after both Document Import and Relation Import finish, for
/// every project touched by the current run - a single CIIR JSONL file can span many distinct
/// projects (spec §5's per-record "Resolve Project" step), so this aggregates one
/// <see cref="IRelationResolver.ResolveAsync"/> call per project rather than assuming a single
/// project per run. A thin wrapper: the actual resolution logic, including the confirmed
/// symbol-match fallback, lives entirely in <see cref="IRelationResolver"/>'s implementation.
/// </summary>
public sealed class ResolveRelations
{
    private readonly IRelationResolver _resolver;

    public ResolveRelations(IRelationResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>Resolves relations for every project in <paramref name="projectIds"/>, summing their counters.</summary>
    /// <param name="projectIds">Every project touched by the current run.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    public async Task<RelationResolutionCounters> ExecuteAsync(
        IReadOnlyCollection<long> projectIds, CancellationToken cancellationToken = default)
    {
        var total = new RelationResolutionCounters();

        foreach (var projectId in projectIds)
        {
            var counters = await _resolver.ResolveAsync(projectId, projectIds, cancellationToken);
            total = Add(total, counters);
        }

        return total;
    }

    private static RelationResolutionCounters Add(RelationResolutionCounters left, RelationResolutionCounters right) => new()
    {
        RelationsTotal = left.RelationsTotal + right.RelationsTotal,
        SourceResolved = left.SourceResolved + right.SourceResolved,
        TargetResolved = left.TargetResolved + right.TargetResolved,
        ExternalTargets = left.ExternalTargets + right.ExternalTargets,
        UnresolvedTargets = left.UnresolvedTargets + right.UnresolvedTargets,
        AmbiguousTargets = left.AmbiguousTargets + right.AmbiguousTargets,
        DynamicTargets = left.DynamicTargets + right.DynamicTargets,
    };
}
