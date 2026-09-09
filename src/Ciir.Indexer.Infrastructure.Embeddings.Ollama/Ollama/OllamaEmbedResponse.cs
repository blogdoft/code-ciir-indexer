using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;

/// <summary>Success response body for <c>POST /api/embed</c>.</summary>
public sealed class OllamaEmbedResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Positionally aligned with the request's <c>input</c> array - no index field.</summary>
    [JsonPropertyName("embeddings")]
    public IReadOnlyList<float[]>? Embeddings { get; init; }
}
