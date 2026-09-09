using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class RelationIdentityKeyGeneratorTests
{
    private readonly RelationIdentityKeyGenerator _sut = new();

    [Fact]
    public void Generate_SameRelationTwice_ProducesTheSameKey()
    {
        var relation = BuildRelation();

        var first = _sut.Generate(1, relation);
        var second = _sut.Generate(1, relation);

        first.ShouldBe(second);
    }

    [Fact]
    public void Generate_TwoOccurrencesAtDifferentLocations_ProduceDifferentKeys()
    {
        var first = _sut.Generate(1, BuildRelation(startLine: 10));
        var second = _sut.Generate(1, BuildRelation(startLine: 20));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Generate_UnresolvedTargetTwiceAtSameLocation_ProducesTheSameKey()
    {
        // The common real-world case: target.id is absent (TargetCiirId null). Two identical
        // occurrences must still deduplicate even though the "natural" key contains nulls.
        var first = _sut.Generate(1, BuildRelation(targetCiirId: null));
        var second = _sut.Generate(1, BuildRelation(targetCiirId: null));

        first.ShouldBe(second);
    }

    [Fact]
    public void Generate_DifferentProject_ProducesADifferentKey()
    {
        var relation = BuildRelation();

        var first = _sut.Generate(1, relation);
        var second = _sut.Generate(2, relation);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Generate_DifferentKind_ProducesADifferentKey()
    {
        var first = _sut.Generate(1, BuildRelation(kind: "calls"));
        var second = _sut.Generate(1, BuildRelation(kind: "reads"));

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Generate_NullRelation_Throws()
    {
        Should.Throw<ArgumentNullException>(() => _sut.Generate(1, null!));
    }

    private static CiirRelation BuildRelation(
        string kind = "calls",
        CiirIdentity? targetCiirId = null,
        int startLine = 10)
    {
        return new CiirRelation
        {
            SourceCiirId = new CiirIdentity("sha256:" + new string('a', 64)),
            TargetCiirId = targetCiirId,
            Kind = kind,
            TargetSymbol = "NS.Type.Member",
            Resolution = new CiirRelationResolution
            {
                Status = RelationResolutionStatus.Resolved,
                Origin = RelationResolutionOrigin.Project,
            },
            SourcePath = "src/Type.cs",
            StartLine = startLine,
            StartColumn = 5,
            EndLine = startLine,
            EndColumn = 20,
        };
    }
}
