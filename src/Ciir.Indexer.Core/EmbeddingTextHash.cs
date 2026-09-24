using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Core;

/// <summary>
/// SHA-256 of a CIIR document's <c>embeddingText</c> ("sha256:&lt;64 hex chars&gt;"), preserved
/// from the CIIR document's own <c>embeddingTextHash</c> field. Identifies the semantic content
/// only - it is not, by itself, the identity of any vector stored for that content (see
/// <see cref="IEmbeddingFingerprintGenerator"/>).
/// </summary>
public readonly record struct EmbeddingTextHash
{
    private EmbeddingTextHash(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static implicit operator string(EmbeddingTextHash hash) => hash.Value;

    /// <summary>Validates and creates an <see cref="EmbeddingTextHash"/> from its wire representation.</summary>
    /// <param name="value">The candidate "sha256:&lt;64 hex chars&gt;" string.</param>
    public static Result<EmbeddingTextHash> Create(string? value)
    {
        var validation = Sha256Value.Create(value, nameof(EmbeddingTextHash));
        return validation.IsSuccess
            ? Result<EmbeddingTextHash>.FromSuccess(new EmbeddingTextHash(validation.Value))
            : Result<EmbeddingTextHash>.FromFailure(validation.Failure);
    }

    public override string ToString() => Value;
}
