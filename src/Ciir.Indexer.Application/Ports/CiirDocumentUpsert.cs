using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// A <see cref="CiirDocument"/> paired with the outcome of the embedding decision (spec's
/// fingerprint addendum). <see cref="Embedding"/> is null both when the document has no
/// <c>embeddingText</c> at all (spec §45) and when an existing vector is being reused unchanged;
/// <see cref="ReuseExistingEmbedding"/> disambiguates the two - true means "keep whatever vector is
/// already stored" (avoids requiring the caller to round-trip vector bytes just to leave them
/// unchanged), false with a null <see cref="Embedding"/> means "there is genuinely no vector for
/// this document" and any previously stored vector must be cleared.
/// </summary>
public sealed record CiirDocumentUpsert
{
    public required CiirDocument Document { get; init; }

    public ReadOnlyMemory<float>? Embedding { get; init; }

    public bool ReuseExistingEmbedding { get; init; }

    public string? EmbeddingModel { get; init; }

    public int? EmbeddingDimensions { get; init; }

    public string? EmbeddingFingerprintHash { get; init; }
}
