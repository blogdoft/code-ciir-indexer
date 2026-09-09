using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAI;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAICompatible;
using Microsoft.Extensions.DependencyInjection;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI;

/// <summary>Wires the OpenAI provider module (OpenAI + generic OpenAI-compatible servers) into the composition root.</summary>
public static class OpenAiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the "OpenAI" and "OpenAICompatible" <see cref="IEmbeddingProviderFactory"/>
    /// implementations, plus the named HTTP clients they resolve at creation time.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    public static IServiceCollection AddOpenAiEmbeddingProviders(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IEmbeddingProviderFactory, OpenAiEmbeddingProviderFactory>();
        services.AddSingleton<IEmbeddingProviderFactory, OpenAiCompatibleEmbeddingProviderFactory>();
        return services;
    }
}
