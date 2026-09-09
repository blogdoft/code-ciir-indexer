using System.Text.Json.Serialization;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;

/// <summary>Error response body Ollama returns on non-2xx status codes.</summary>
public sealed class OllamaErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
