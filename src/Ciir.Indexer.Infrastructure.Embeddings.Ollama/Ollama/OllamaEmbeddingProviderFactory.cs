using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;

/// <summary>Registers the "Ollama" provider with the <see cref="EmbeddingGeneratorResolver"/>.</summary>
public sealed class OllamaEmbeddingProviderFactory : IEmbeddingProviderFactory
{
    public string ProviderName => "Ollama";

    public void Validate(EmbeddingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ConfigurationValidationException("Embeddings:BaseUrl is required for the Ollama provider.");
        }
    }

    public IEmbeddingGenerator Create(EmbeddingOptions options, IServiceProvider serviceProvider)
    {
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OllamaEmbeddingGenerator));
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        return new OllamaEmbeddingGenerator(httpClient, options, loggerFactory.CreateLogger<OllamaEmbeddingGenerator>());
    }
}
