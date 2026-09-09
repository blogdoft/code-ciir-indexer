using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAICompatible;

/// <summary>Registers the "OpenAICompatible" provider with the <see cref="EmbeddingGeneratorResolver"/>.</summary>
public sealed class OpenAiCompatibleEmbeddingProviderFactory : IEmbeddingProviderFactory
{
    public string ProviderName => "OpenAICompatible";

    public void Validate(EmbeddingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ConfigurationValidationException("Embeddings:BaseUrl is required for the OpenAICompatible provider.");
        }
    }

    public IEmbeddingGenerator Create(EmbeddingOptions options, IServiceProvider serviceProvider)
    {
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAiCompatibleEmbeddingGenerator));
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        return new OpenAiCompatibleEmbeddingGenerator(httpClient, options, loggerFactory.CreateLogger<OpenAiCompatibleEmbeddingGenerator>());
    }
}
