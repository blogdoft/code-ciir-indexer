using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAI;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests;

public sealed class OpenAiEmbeddingProviderFactoryTests
{
    private readonly OpenAiEmbeddingProviderFactory _sut = new();

    [Fact]
    public void Validate_ApiKeyConfigured_DoesNotThrow()
    {
        var options = BuildOptions();
        options.ApiKey = "sk-test";

        Should.NotThrow(() => _sut.Validate(options));
    }

    [Fact]
    public void Validate_NoApiKeyAnywhere_Throws()
    {
        var options = BuildOptions();
        options.ApiKey = null;

        var originalEnvironmentValue = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        try
        {
            Should.Throw<ConfigurationValidationException>(() => _sut.Validate(options));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalEnvironmentValue);
        }
    }

    private static EmbeddingOptions BuildOptions() => new()
    {
        Provider = "OpenAI",
        Model = "text-embedding-3-small",
        Dimensions = 1536,
    };
}
