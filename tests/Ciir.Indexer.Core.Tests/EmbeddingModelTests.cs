using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class EmbeddingModelTests
{
    [Fact]
    public void Constructor_ValidNameAndDimensions_Succeeds()
    {
        var model = new EmbeddingModel("bge-m3", 1024);

        model.Name.ShouldBe("bge-m3");
        model.Dimensions.ShouldBe(1024);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyName_Throws(string? name)
    {
        Should.Throw<ArgumentException>(() => new EmbeddingModel(name!, 1024));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveDimensions_Throws(int dimensions)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EmbeddingModel("bge-m3", dimensions));
    }
}
