using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

internal sealed class OpenAiEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingDataItem>? Data { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }
}
