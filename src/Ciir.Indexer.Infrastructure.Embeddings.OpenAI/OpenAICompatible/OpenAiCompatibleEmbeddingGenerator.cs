using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAICompatible;

/// <summary>
/// Embedding provider for any self-hosted or third-party server exposing an OpenAI-compatible
/// "POST {BaseUrl}/embeddings" endpoint (text-embeddings-inference, vLLM, LM Studio, llama.cpp
/// server, Ollama's own OpenAI-compatible route, etc). BaseUrl is required; ApiKey is optional
/// since many local deployments don't authenticate.
/// </summary>
public sealed class OpenAiCompatibleEmbeddingGenerator : OpenAiStyleEmbeddingGenerator
{
    public OpenAiCompatibleEmbeddingGenerator(HttpClient httpClient, EmbeddingOptions options, ILogger<OpenAiCompatibleEmbeddingGenerator> logger)
        : base(
            Configure(httpClient, options),
            provider: "OpenAICompatible",
            model: options.Model,
            expectedDimensions: options.Dimensions,
            batchSize: options.BatchSize,
            normalize: options.Normalize,
            maxRetries: options.MaxRetries,
            logger)
    {
    }

    private static HttpClient Configure(HttpClient httpClient, EmbeddingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ConfigurationValidationException("Embeddings:BaseUrl is required for the OpenAICompatible provider.");
        }

        httpClient.BaseAddress = new Uri(NormalizeBaseUrl(options.BaseUrl));
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        ApplyBearerToken(httpClient, options.ApiKey);
        return httpClient;
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
}
