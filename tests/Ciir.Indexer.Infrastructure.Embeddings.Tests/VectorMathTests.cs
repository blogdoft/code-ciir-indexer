using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests;

public sealed class VectorMathTests
{
    [Fact]
    public void NormalizeInPlace_NonUnitVector_ProducesAUnitLengthVector()
    {
        float[] vector = [3f, 4f];

        VectorMath.NormalizeInPlace(vector);

        vector[0].ShouldBe(0.6f, tolerance: 0.0001);
        vector[1].ShouldBe(0.8f, tolerance: 0.0001);
    }

    [Fact]
    public void NormalizeInPlace_AlreadyUnitVector_IsUnchanged()
    {
        float[] vector = [1f, 0f];

        VectorMath.NormalizeInPlace(vector);

        vector[0].ShouldBe(1f);
        vector[1].ShouldBe(0f);
    }

    [Fact]
    public void NormalizeInPlace_AllZeroVector_IsLeftUnchanged()
    {
        float[] vector = [0f, 0f, 0f];

        VectorMath.NormalizeInPlace(vector);

        vector.ShouldBe([0f, 0f, 0f]);
    }
}
