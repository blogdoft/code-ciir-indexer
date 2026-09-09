using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAI;

/// <summary>
/// Embedding provider for the official OpenAI embeddings API (never chat/completion - only
/// POST /v1/embeddings). API key comes from Embeddings:ApiKey or the OPENAI_API_KEY environment
/// variable, resolved by <see cref="EmbeddingOptionsValidator.ResolveOpenAiApiKey"/>.
/// </summary>
public sealed class OpenAiEmbeddingGenerator : OpenAiStyleEmbeddingGenerator
{
    // S1075 (hardcoded URI) does not apply here: this is a well-known, publicly documented API
    // endpoint used only as an overridable fallback (Embeddings:BaseUrl always wins when set) -
    // not an environment-specific path the rule is meant to catch.
#pragma warning disable S1075
    private const string DefaultBaseUrl = "https://api.openai.com/v1/";
#pragma warning restore S1075

    public OpenAiEmbeddingGenerator(HttpClient httpClient, EmbeddingOptions options, ILogger<OpenAiEmbeddingGenerator> logger)
        : base(
            Configure(httpClient, options),
            provider: "OpenAI",
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
        httpClient.BaseAddress = new Uri(NormalizeBaseUrl(options.BaseUrl ?? DefaultBaseUrl));
        httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        ApplyBearerToken(httpClient, EmbeddingOptionsValidator.ResolveOpenAiApiKey(options));
        return httpClient;
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
}
