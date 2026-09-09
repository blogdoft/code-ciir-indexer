using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests;

public sealed class EmbeddingOptionsValidatorTests
{
    private static readonly string[] KnownProviders = ["Ollama", "OpenAI"];

    [Fact]
    public void Validate_ValidOptions_DoesNotThrow()
    {
        var options = BuildOptions();

        Should.NotThrow(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Fact]
    public void Validate_MissingProvider_Throws()
    {
        var options = BuildOptions();
        options.Provider = string.Empty;

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Fact]
    public void Validate_UnknownProvider_Throws()
    {
        var options = BuildOptions();
        options.Provider = "SomeOtherProvider";

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Fact]
    public void Validate_MissingModel_Throws()
    {
        var options = BuildOptions();
        options.Model = string.Empty;

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveDimensions_Throws(int dimensions)
    {
        var options = BuildOptions();
        options.Dimensions = dimensions;

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveBatchSize_Throws(int batchSize)
    {
        var options = BuildOptions();
        options.BatchSize = batchSize;

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Fact]
    public void Validate_NegativeMaxRetries_Throws()
    {
        var options = BuildOptions();
        options.MaxRetries = -1;

        Should.Throw<ConfigurationValidationException>(() => EmbeddingOptionsValidator.Validate(options, KnownProviders));
    }

    [Fact]
    public void ResolveOpenAiApiKey_ExplicitKeyConfigured_PrefersItOverTheEnvironmentVariable()
    {
        var options = BuildOptions();
        options.ApiKey = "explicit-key";

        EmbeddingOptionsValidator.ResolveOpenAiApiKey(options).ShouldBe("explicit-key");
    }

    private static EmbeddingOptions BuildOptions() => new()
    {
        Provider = "Ollama",
        Model = "bge-m3",
        Dimensions = 1024,
        BatchSize = 32,
        TimeoutSeconds = 60,
        MaxRetries = 3,
    };
}
