using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

internal sealed class OpenAiErrorDetail
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
