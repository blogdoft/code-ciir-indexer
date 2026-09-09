namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>Small shared numeric helpers so every provider normalizes (or doesn't) the same way.</summary>
public static class VectorMath
{
    /// <summary>
    /// L2-normalizes a vector in place. A no-op (mathematically idempotent) if the vector is
    /// already unit length, so it is safe to apply even to providers that already normalize - but
    /// each vector is only ever normalized once per run, at the single call site in each provider,
    /// to keep the normalization policy deterministic and easy to audit.
    /// </summary>
    /// <param name="vector">The vector to normalize in place.</param>
    public static void NormalizeInPlace(Span<float> vector)
    {
        double sumSquares = 0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        if (sumSquares <= 0)
        {
            return;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        if (norm is 0f or 1f)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }
    }
}
