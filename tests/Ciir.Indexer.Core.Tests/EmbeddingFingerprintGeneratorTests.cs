using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class EmbeddingFingerprintGeneratorTests
{
    private readonly EmbeddingFingerprintGenerator _sut = new();

    [Fact]
    public void Generate_KnownInputTriple_ProducesExactlyTheIndependentlyComputedSha256()
    {
        // Golden value computed independently outside this codebase:
        //   hashlib.sha256("sha256:aaa...a\nbge-m3\n1024".encode("utf-8")).hexdigest()
        // Pins the composition format so a future refactor can never silently change it.
        var embeddingTextHash = "sha256:" + new string('a', 64);

        var fingerprint = _sut.Generate(embeddingTextHash, "bge-m3", 1024);

        fingerprint.ShouldBe("sha256:d47cac946fc836238a0834a4fd5c443d349201fa1ef667ab7c55fc4bb888d1aa");
    }

    [Fact]
    public void Generate_SameTextHashModelAndDimensions_ProducesTheSameFingerprint()
    {
        var textHash = "sha256:" + new string('b', 64);

        var first = _sut.Generate(textHash, "bge-m3", 1024);
        var second = _sut.Generate(textHash, "bge-m3", 1024);

        first.ShouldBe(second);
    }

    [Fact]
    public void Generate_DifferentTextHash_ProducesADifferentFingerprint()
    {
        var original = _sut.Generate("sha256:" + new string('c', 64), "bge-m3", 1024);
        var changed = _sut.Generate("sha256:" + new string('d', 64), "bge-m3", 1024);

        changed.ShouldNotBe(original);
    }

    [Fact]
    public void Generate_DifferentModel_ProducesADifferentFingerprint()
    {
        var textHash = "sha256:" + new string('e', 64);

        var original = _sut.Generate(textHash, "bge-m3", 1024);
        var changed = _sut.Generate(textHash, "nomic-embed-text", 1024);

        changed.ShouldNotBe(original);
    }

    [Fact]
    public void Generate_DifferentDimensions_ProducesADifferentFingerprint()
    {
        var textHash = "sha256:" + new string('f', 64);

        var original = _sut.Generate(textHash, "bge-m3", 1024);
        var changed = _sut.Generate(textHash, "bge-m3", 768);

        changed.ShouldNotBe(original);
    }

    [Fact]
    public void Generate_Always_ProducesTheSha256PrefixedLowercaseHexFormat()
    {
        var fingerprint = _sut.Generate("sha256:" + new string('0', 64), "any-model", 1);

        fingerprint.ShouldMatch("^sha256:[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_EmptyEmbeddingTextHash_Throws(string? embeddingTextHash)
    {
        Should.Throw<ArgumentException>(() => _sut.Generate(embeddingTextHash!, "bge-m3", 1024));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_EmptyModel_Throws(string? model)
    {
        Should.Throw<ArgumentException>(() => _sut.Generate("sha256:" + new string('a', 64), model!, 1024));
    }
}
