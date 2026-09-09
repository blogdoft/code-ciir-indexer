using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class ResolveRelationsTests
{
    private readonly IRelationResolver _resolver = Substitute.For<IRelationResolver>();

    [Fact]
    public async Task ExecuteAsync_SingleProject_ReturnsThatProjectsCounters()
    {
        var counters = new RelationResolutionCounters { RelationsTotal = 10, TargetResolved = 7, UnresolvedTargets = 3 };
        _resolver.ResolveAsync(1, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(counters);

        var result = await CreateSut().ExecuteAsync([1]);

        result.ShouldBe(counters);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleProjects_SumsEachCounterAcrossProjects()
    {
        _resolver.ResolveAsync(1, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(new RelationResolutionCounters
        {
            RelationsTotal = 10,
            SourceResolved = 10,
            TargetResolved = 6,
            ExternalTargets = 2,
            UnresolvedTargets = 1,
            AmbiguousTargets = 1,
            DynamicTargets = 0,
        });
        _resolver.ResolveAsync(2, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(new RelationResolutionCounters
        {
            RelationsTotal = 5,
            SourceResolved = 5,
            TargetResolved = 3,
            ExternalTargets = 0,
            UnresolvedTargets = 1,
            AmbiguousTargets = 0,
            DynamicTargets = 1,
        });

        var result = await CreateSut().ExecuteAsync([1, 2]);

        result.RelationsTotal.ShouldBe(15);
        result.SourceResolved.ShouldBe(15);
        result.TargetResolved.ShouldBe(9);
        result.ExternalTargets.ShouldBe(2);
        result.UnresolvedTargets.ShouldBe(2);
        result.AmbiguousTargets.ShouldBe(1);
        result.DynamicTargets.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesEachProjectExactlyOnce()
    {
        _resolver.ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Returns(new RelationResolutionCounters());

        await CreateSut().ExecuteAsync([1, 2, 3]);

        await _resolver.Received(1).ResolveAsync(1, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _resolver.Received(1).ResolveAsync(2, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _resolver.Received(1).ResolveAsync(3, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoProjects_ReturnsZeroedCountersWithoutCallingTheResolver()
    {
        var result = await CreateSut().ExecuteAsync([]);

        result.RelationsTotal.ShouldBe(0);
        await _resolver.DidNotReceive().ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    private ResolveRelations CreateSut() => new(_resolver);
}
