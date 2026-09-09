namespace Ciir.Indexer.Core;

/// <summary>
/// The identity of an embedding model configuration: a vector can only be interpreted correctly
/// together with the model (and dimensionality) that produced it - embeddings from different
/// models must never be compared as if they belonged to the same vector space.
/// </summary>
public sealed record EmbeddingModel(string Name, int Dimensions)
{
    public string Name { get; init; } = !string.IsNullOrWhiteSpace(Name)
        ? Name
        : throw new ArgumentException("Embedding model name must not be empty.", nameof(Name));

    public int Dimensions { get; init; } = Dimensions > 0
        ? Dimensions
        : throw new ArgumentOutOfRangeException(nameof(Dimensions), Dimensions, "Embedding dimensions must be positive.");
}
