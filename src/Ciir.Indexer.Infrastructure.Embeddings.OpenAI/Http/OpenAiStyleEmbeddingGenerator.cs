using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.Http;

/// <summary>
/// Base implementation for any provider speaking the OpenAI "POST {baseUrl}/embeddings" wire
/// format: the public OpenAI API itself, and any self-hosted/third-party server that mirrors it
/// (text-embeddings-inference, vLLM, LM Studio, llama.cpp server, etc). Owns retry-with-backoff
/// (via the shared <see cref="HttpRetryPolicy"/>) and dimension validation; provider-specific
/// classes only configure the HTTP client (base address, auth header).
/// </summary>
public abstract class OpenAiStyleEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly bool _normalize;
    private readonly HttpRetryPolicy _retryPolicy;

    protected OpenAiStyleEmbeddingGenerator(
        HttpClient httpClient,
        string provider,
        string model,
        int expectedDimensions,
        int batchSize,
        bool normalize,
        int maxRetries,
        ILogger logger)
    {
        _httpClient = httpClient;
        Provider = provider;
        Model = model;
        Dimensions = expectedDimensions;
        BatchSize = batchSize;
        _normalize = normalize;
        _retryPolicy = new HttpRetryPolicy(provider, maxRetries, logger);
    }

    public string Provider { get; }

    public string Model { get; }

    public int Dimensions { get; }

    public int BatchSize { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddingResult>> GenerateAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var request = new OpenAiEmbeddingRequest { Model = Model, Input = texts };

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync("embeddings", request, cancellationToken);
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

                // Permanent failure (400/401/403/404/422/...): fail fast, never retried.
                var errorMessage = await ExtractErrorMessageAsync(response, cancellationToken);
                throw new EmbeddingGenerationException(
                    $"Embedding provider '{Provider}' rejected the request with HTTP {(int)response.StatusCode}: {errorMessage}",
                    wasTransient: false);
            }
        }
    }

    /// <summary>
    /// Applies a bearer token to the given client if one is present. Kept static/protected so both
    /// concrete providers configure auth identically without duplicating the header-setting code.
    /// </summary>
    /// <param name="httpClient">The client to configure.</param>
    /// <param name="apiKey">The API key to apply, or null/empty to skip authentication.</param>
    protected static void ApplyBearerToken(HttpClient httpClient, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await SafeReadBodyAsync(response, cancellationToken);
        try
        {
            var error = JsonSerializer.Deserialize<OpenAiErrorResponse>(body);
            if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
            {
                return error.Error.Message;
            }
        }
        catch (JsonException)
        {
            // Fall through to raw body below - not every OpenAI-compatible server wraps errors the same way.
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

    private async Task<IReadOnlyList<EmbeddingResult>> ParseResponseAsync(
        HttpResponseMessage response, int expectedCount, CancellationToken cancellationToken)
    {
        OpenAiEmbeddingResponse? parsed;
        try
        {
            parsed = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new EmbeddingGenerationException(
                $"Embedding provider '{Provider}' returned a response that could not be parsed: {ex.Message}",
                wasTransient: false,
                ex);
        }

        if (parsed?.Data is null || parsed.Data.Count != expectedCount)
        {
            throw new EmbeddingGenerationException(
                $"Embedding provider '{Provider}' returned {parsed?.Data?.Count ?? 0} vector(s) for {expectedCount} input text(s).",
                wasTransient: false);
        }

        var ordered = new EmbeddingResult[expectedCount];
        foreach (var item in parsed.Data)
        {
            if (item.Embedding is null || item.Index < 0 || item.Index >= expectedCount)
            {
                throw new EmbeddingGenerationException(
                    $"Embedding provider '{Provider}' returned a malformed embedding entry (index {item.Index}).",
                    wasTransient: false);
            }

            var vector = item.Embedding;
            var dimensions = vector.Length;
            if (Dimensions != dimensions)
            {
                throw new EmbeddingDimensionMismatchException(Provider, Model, Dimensions, dimensions);
            }

            if (_normalize)
            {
                VectorMath.NormalizeInPlace(vector);
            }

            ordered[item.Index] = new EmbeddingResult(vector, Provider, Model, dimensions);
        }

        return ordered;
    }
}
