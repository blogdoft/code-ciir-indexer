namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// A provider returned a vector whose dimensionality did not match the configured
/// dimensionality for this model. Never truncated/padded/silently accepted (spec §10).
/// </summary>
public sealed class EmbeddingDimensionMismatchException : Exception
{
    public EmbeddingDimensionMismatchException(string provider, string model, int expected, int actual)
        : base($"Embedding dimension mismatch for provider '{provider}', model '{model}': expected {expected}, got {actual}.")
    {
    }

    public EmbeddingDimensionMismatchException()
    {
    }

    public EmbeddingDimensionMismatchException(string message)
        : base(message)
    {
    }

    public EmbeddingDimensionMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
