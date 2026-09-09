using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

/// <summary>Request shape for the OpenAI "POST /embeddings" wire format.</summary>
internal sealed class OpenAiEmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<string> Input { get; init; }
}
