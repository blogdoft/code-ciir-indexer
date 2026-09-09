using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;

/// <summary>
/// Embedding provider for a native Ollama server (https://ollama.com/), speaking Ollama's own
/// wire format - <c>POST {BaseUrl}/api/embed</c> - rather than routing through the generic
/// OpenAI-compatible provider. Ollama's <c>/api/embed</c> endpoint accepts a batch of texts in one
/// call (<c>input: [...]</c>) and returns one vector per text, aligned by position (no index field
/// in the response, unlike the OpenAI wire format).
/// </summary>
public sealed class OllamaEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly bool _normalize;
    private readonly HttpRetryPolicy _retryPolicy;

    public OllamaEmbeddingGenerator(HttpClient httpClient, EmbeddingOptions options, ILogger<OllamaEmbeddingGenerator> logger)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ConfigurationValidationException("Embeddings:BaseUrl is required for the Ollama provider.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(NormalizeBaseUrl(options.BaseUrl));
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        Model = options.Model;
        Dimensions = options.Dimensions;
        BatchSize = options.BatchSize;
        _normalize = options.Normalize;
        _retryPolicy = new HttpRetryPolicy(Provider, options.MaxRetries, logger);
    }

    public string Provider => "Ollama";

    public string Model { get; }

    public int Dimensions { get; }

    public int BatchSize { get; }

    public async Task<IReadOnlyList<EmbeddingResult>> GenerateAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var request = new OllamaEmbedRequest { Model = Model, Input = texts };

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync("api/embed", request, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!await _retryPolicy.ShouldRetryAsync(attempt, "request timed out", cancellationToken).ConfigureAwait(false))
                {
                    throw new EmbeddingGenerationException(
                        $"Embedding request to provider '{Provider}' timed out after {attempt + 1} attempt(s).",
                        wasTransient: true);
                }

                attempt++;
                continue;
            }
            catch (HttpRequestException ex)
            {
                if (!await _retryPolicy.ShouldRetryAsync(attempt, ex.Message, cancellationToken).ConfigureAwait(false))
                {
                    throw new EmbeddingGenerationException(
                        $"Embedding request to provider '{Provider}' failed after {attempt + 1} attempt(s): {ex.Message}",
                        wasTransient: true,
                        ex);
                }

                attempt++;
                continue;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return await ParseResponseAsync(response, texts.Count, cancellationToken);
                }

                if (HttpRetryPolicy.IsTransientStatusCode(response.StatusCode))
                {
                    var body = await SafeReadBodyAsync(response, cancellationToken);
                    if (!await _retryPolicy.ShouldRetryAsync(attempt, $"HTTP {(int)response.StatusCode}: {body}", cancellationToken).ConfigureAwait(false))
                    {
                        throw new EmbeddingGenerationException(
                            $"Embedding provider '{Provider}' returned {(int)response.StatusCode} after {attempt + 1} attempt(s): {body}",
                            wasTransient: true);
                    }

                    attempt++;
                    continue;
                }

                // Permanent failure (400/404/...): fail fast, never retried.
                var errorMessage = await ExtractErrorMessageAsync(response, cancellationToken);
                throw new EmbeddingGenerationException(
                    $"Embedding provider '{Provider}' rejected the request with HTTP {(int)response.StatusCode}: {errorMessage}",
                    wasTransient: false);
            }
        }
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await SafeReadBodyAsync(response, cancellationToken);
        try
        {
            var error = JsonSerializer.Deserialize<OllamaErrorResponse>(body);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch (JsonException)
        {
            // Fall through to raw body below.
        }

        return body;
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return $"<unable to read response body: {ex.Message}>";
        }
    }

    private static string NormalizeBaseUrl(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

    private async Task<IReadOnlyList<EmbeddingResult>> ParseResponseAsync(
        HttpResponseMessage response, int expectedCount, CancellationToken cancellationToken)
    {
        OllamaEmbedResponse? parsed;
        try
        {
            parsed = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new EmbeddingGenerationException(
                $"Embedding provider '{Provider}' returned a response that could not be parsed: {ex.Message}",
                wasTransient: false,
                ex);
        }

        if (parsed?.Embeddings is null || parsed.Embeddings.Count != expectedCount)
        {
            throw new EmbeddingGenerationException(
                $"Embedding provider '{Provider}' returned {parsed?.Embeddings?.Count ?? 0} vector(s) for {expectedCount} input text(s).",
                wasTransient: false);
        }

        var results = new EmbeddingResult[expectedCount];
        for (var i = 0; i < expectedCount; i++)
        {
            var vector = parsed.Embeddings[i];
            var dimensions = vector.Length;
            if (Dimensions != dimensions)
            {
                throw new EmbeddingDimensionMismatchException(Provider, Model, Dimensions, dimensions);
            }

            if (_normalize)
            {
                VectorMath.NormalizeInPlace(vector);
            }

            results[i] = new EmbeddingResult(vector, Provider, Model, dimensions);
        }

        return results;
    }
}
