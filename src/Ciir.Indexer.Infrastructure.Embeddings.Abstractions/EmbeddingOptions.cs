namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// Strongly typed binding of the "Embeddings" configuration section. Selects and configures
/// exactly one <see cref="Application.Ports.IEmbeddingGenerator"/> provider.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>
    /// Name of the registered <see cref="IEmbeddingProviderFactory"/> to use (compared
    /// case-insensitively), e.g. Ollama, OpenAI. The exact set depends on which provider modules
    /// the composition root registered.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model name/identifier passed to the provider (e.g. "bge-m3", "text-embedding-3-small").</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Expected vector dimensionality - always validated against what the provider actually
    /// returns (spec §10); never inferred, unlike a per-model-multi-dimension design.
    /// </summary>
    public int Dimensions { get; set; }

    /// <summary>Base URL for HTTP providers.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>API key for HTTP providers. Falls back to OPENAI_API_KEY for the OpenAI provider.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whether to L2-normalize generated vectors. Applied at most once, after generation.</summary>
    public bool Normalize { get; set; } = true;

    /// <summary>Maximum number of texts sent to the provider per embedding call.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>HTTP request timeout, in seconds, for HTTP-based providers.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Maximum retry attempts for transient HTTP failures (429/502/503/504/timeouts).</summary>
    public int MaxRetries { get; set; } = 3;
}
