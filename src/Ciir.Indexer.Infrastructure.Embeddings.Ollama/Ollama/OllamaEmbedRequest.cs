using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;

/// <summary>Request body for Ollama's <c>POST /api/embed</c> endpoint.</summary>
public sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>One or more input texts. Ollama returns one vector per input, in the same order.</summary>
    [JsonPropertyName("input")]
    public required IReadOnlyList<string> Input { get; init; }
}
