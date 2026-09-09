using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;
using Microsoft.Extensions.DependencyInjection;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama;

/// <summary>Wires the native Ollama provider module into the composition root.</summary>
public static class OllamaServiceCollectionExtensions
{
    /// <summary>Registers the "Ollama" <see cref="IEmbeddingProviderFactory"/> and its named HTTP client.</summary>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddOllamaEmbeddingProvider(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IEmbeddingProviderFactory, OllamaEmbeddingProviderFactory>();
        return services;
    }
}
