using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

internal sealed class OpenAiErrorResponse
{
    [JsonPropertyName("error")]
    public OpenAiErrorDetail? Error { get; init; }
}
