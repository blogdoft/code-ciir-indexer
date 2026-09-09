namespace Ciir.Indexer.Core;

/// <summary>
/// SHA-256 of a CIIR document's <c>embeddingText</c> ("sha256:&lt;64 hex chars&gt;"), preserved
/// from the CIIR document's own <c>embeddingTextHash</c> field. Identifies the semantic content
/// only - it is not, by itself, the identity of any vector stored for that content (see
/// <see cref="IEmbeddingFingerprintGenerator"/>).
/// </summary>
public readonly record struct EmbeddingTextHash
{
    public EmbeddingTextHash(string value)
    {
        Value = Sha256Value.Validate(value, nameof(value));
    }

    public string Value { get; }

    public static implicit operator string(EmbeddingTextHash hash) => hash.Value;

    public override string ToString() => Value;
}
