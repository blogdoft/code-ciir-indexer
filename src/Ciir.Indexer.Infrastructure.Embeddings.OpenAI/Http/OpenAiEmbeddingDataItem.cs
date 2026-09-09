using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

internal sealed class OpenAiEmbeddingDataItem
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; init; }
}
