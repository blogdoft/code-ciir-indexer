namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Turns plain text (a document's <c>embeddingText</c> - never the full JSON document, spec §6)
/// into vectors. The rest of the application depends only on this interface, never on a concrete
/// provider (spec §34).
/// </summary>
public interface IEmbeddingGenerator
{
    string Provider { get; }

    string Model { get; }

    /// <summary>
    /// The configured/validated vector dimensionality (spec §10). Exposed here - not only observed
    /// from a generated result - because the embedding fingerprint decision (spec's fingerprint
    /// addendum) must be computable before ever calling <see cref="GenerateAsync"/>.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// The maximum number of texts this generator wants per <see cref="GenerateAsync"/> call (spec
    /// §32's "EmbeddingBatchSize"). Callers should chunk larger sets of texts accordingly.
    /// </summary>
    int BatchSize { get; }

    /// <summary>
    /// Generates one embedding per input text, preserving order: result[i] corresponds to texts[i].
    /// Implementations may batch internally up to their provider's limits; callers should still
    /// respect <see cref="BatchSize"/> when choosing how many texts to pass per call (spec §32).
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<IReadOnlyList<EmbeddingResult>> GenerateAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
