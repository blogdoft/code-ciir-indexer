namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// Validates the parts of <see cref="EmbeddingOptions"/> that are common to every provider, so
/// failures surface as a clear <see cref="ConfigurationValidationException"/> before any file is
/// read or any network/database call is made. Provider-specific requirements (e.g. a required
/// BaseUrl or API key) are validated separately by each provider's
/// <see cref="IEmbeddingProviderFactory.Validate"/> - this class never hardcodes a list of
/// provider names, so adding a new provider module never requires changing it.
/// </summary>
public static class EmbeddingOptionsValidator
{
    /// <summary>
    /// Validates the generic fields of <paramref name="options"/>. <paramref name="knownProviders"/>
    /// is the set of provider names actually registered (one per installed provider module).
    /// </summary>
    /// <param name="options">The embedding configuration to validate.</param>
    /// <param name="knownProviders">The provider names actually registered.</param>
    public static void Validate(EmbeddingOptions options, IReadOnlyCollection<string> knownProviders)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            throw new ConfigurationValidationException(
                $"Embeddings:Provider is required. Known providers: {string.Join(", ", knownProviders)}.");
        }

        if (!knownProviders.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConfigurationValidationException(
                $"Unknown Embeddings:Provider '{options.Provider}'. Known providers: {string.Join(", ", knownProviders)}.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ConfigurationValidationException("Embeddings:Model is required.");
        }

        if (options.Dimensions <= 0)
        {
            throw new ConfigurationValidationException("Embeddings:Dimensions must be greater than zero.");
        }

        if (options.BatchSize <= 0)
        {
            throw new ConfigurationValidationException("Embeddings:BatchSize must be greater than zero.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            throw new ConfigurationValidationException("Embeddings:TimeoutSeconds must be greater than zero.");
        }

        if (options.MaxRetries < 0)
        {
            throw new ConfigurationValidationException("Embeddings:MaxRetries cannot be negative.");
        }
    }

    /// <summary>
    /// Resolves the OpenAI API key, preferring an explicit configuration value over the
    /// conventional OPENAI_API_KEY environment variable. Shared by the OpenAI provider module and
    /// its own validation, and kept here (rather than duplicated) since both need identical
    /// resolution order.
    /// </summary>
    /// <param name="options">The embedding configuration to resolve the API key from.</param>
    public static string? ResolveOpenAiApiKey(EmbeddingOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY");
}
