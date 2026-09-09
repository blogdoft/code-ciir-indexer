namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// The embedding provider could not be reached or returned an error, after retries where
/// applicable. Always carries whether the failure looked transient (for logging/diagnostics);
/// retries themselves already happened, if warranted, before this is thrown.
/// </summary>
public class EmbeddingGenerationException : Exception
{
    public EmbeddingGenerationException(string message, bool wasTransient)
        : base(message)
    {
        WasTransient = wasTransient;
    }

    public EmbeddingGenerationException(string message, bool wasTransient, Exception inner)
        : base(message, inner)
    {
        WasTransient = wasTransient;
    }

    public EmbeddingGenerationException()
    {
    }

    public EmbeddingGenerationException(string message)
        : base(message)
    {
    }

    public EmbeddingGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public bool WasTransient { get; }
}
