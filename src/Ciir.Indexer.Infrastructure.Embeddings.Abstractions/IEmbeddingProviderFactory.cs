using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// Contributed by each embedding provider module (Ollama, OpenAI, ...) to plug itself into the
/// application without the composition root ever branching on a concrete provider type. This is
/// the seam that gives provider selection real dependency inversion: the resolver depends only on
/// this interface, and adding a new provider module never requires changing this assembly (spec §34).
/// </summary>
public interface IEmbeddingProviderFactory
{
    /// <summary>The value of Embeddings:Provider this factory handles (compared case-insensitively).</summary>
    string ProviderName { get; }

    /// <summary>
    /// Validates the provider-specific parts of <paramref name="options"/> (e.g. a required
    /// BaseUrl or API key). Throws <see cref="ConfigurationValidationException"/> on failure.
    /// Generic, provider-agnostic validation already happened in
    /// <see cref="EmbeddingOptionsValidator"/> before this is called.
    /// </summary>
    /// <param name="options">The embedding configuration to validate.</param>
    void Validate(EmbeddingOptions options);

    /// <summary>Constructs the <see cref="IEmbeddingGenerator"/> for this provider.</summary>
    /// <param name="options">The embedding configuration, already validated.</param>
    /// <param name="serviceProvider">Resolves collaborators (e.g. a named HTTP client).</param>
    IEmbeddingGenerator Create(EmbeddingOptions options, IServiceProvider serviceProvider);
}
