using Shouldly;

namespace Ciir.Indexer.Core.Tests;

public sealed class EmbeddingModelTests
{
    [Fact]
    public void Should_CreateModel_When_NameAndDimensionsAreValid()
    {
        var result = EmbeddingModel.Create("bge-m3", 1024);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("bge-m3");
        result.Value.Dimensions.ShouldBe(1024);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ReturnFailure_When_NameIsEmpty(string? name)
    {
        var result = EmbeddingModel.Create(name, 1024);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-embedding-model-invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_ReturnFailure_When_DimensionsAreNotPositive(int dimensions)
    {
        var result = EmbeddingModel.Create("bge-m3", dimensions);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-embedding-model-invalid");
    }
}
