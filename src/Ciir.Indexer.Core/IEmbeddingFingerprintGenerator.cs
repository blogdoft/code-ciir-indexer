namespace Ciir.Indexer.Core;

/// <summary>
/// Computes the fingerprint that decides whether a stored embedding vector is still valid.
/// <c>embeddingTextHash</c> alone answers "did the semantic content change?" - the fingerprint
/// answers "does the stored vector still represent that content under the currently configured
/// embedding model and dimensionality?" These two questions must stay distinct.
/// </summary>
public interface IEmbeddingFingerprintGenerator
{
    /// <summary>
    /// Computes "sha256:&lt;64 hex chars&gt;" over the canonical form
    /// "&lt;embeddingTextHash&gt;\n&lt;model&gt;\n&lt;dimensions&gt;" (UTF-8, invariant-culture
    /// decimal dimensions). Reusing a stored vector is valid exactly when this value matches the
    /// fingerprint stored alongside it.
    /// </summary>
    /// <param name="embeddingTextHash">The CIIR document's own "sha256:&lt;hex&gt;" content hash.</param>
    /// <param name="model">The embedding model name currently configured for the project.</param>
    /// <param name="dimensions">The embedding dimensionality currently configured for the project.</param>
    string Generate(string embeddingTextHash, string model, int dimensions);
}
