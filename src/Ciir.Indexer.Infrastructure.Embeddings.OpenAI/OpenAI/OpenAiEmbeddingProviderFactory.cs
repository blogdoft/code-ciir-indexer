using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAI;

/// <summary>Registers the "OpenAI" provider with the <see cref="EmbeddingGeneratorResolver"/>.</summary>
public sealed class OpenAiEmbeddingProviderFactory : IEmbeddingProviderFactory
{
    public string ProviderName => "OpenAI";

    public void Validate(EmbeddingOptions options)
    {
        var apiKey = EmbeddingOptionsValidator.ResolveOpenAiApiKey(options);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ConfigurationValidationException(
                "No OpenAI API key found. Set Embeddings:ApiKey in configuration or the OPENAI_API_KEY environment variable.");
        }
    }

    public IEmbeddingGenerator Create(EmbeddingOptions options, IServiceProvider serviceProvider)
    {
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenAiEmbeddingGenerator));
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        return new OpenAiEmbeddingGenerator(httpClient, options, loggerFactory.CreateLogger<OpenAiEmbeddingGenerator>());
    }
}
