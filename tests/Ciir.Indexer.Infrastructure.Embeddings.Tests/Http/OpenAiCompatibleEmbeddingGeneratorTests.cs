using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAICompatible;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests.Http;

public sealed class OpenAiCompatibleEmbeddingGeneratorTests
{
    [Fact]
    public void Constructor_MissingBaseUrl_Throws()
    {
        var options = BuildOptions();
        options.BaseUrl = null;

        Should.Throw<ConfigurationValidationException>(
            () => new OpenAiCompatibleEmbeddingGenerator(new HttpClient(), options, NullLogger<OpenAiCompatibleEmbeddingGenerator>.Instance));
    }

    [Fact]
    public void Constructor_NoApiKey_StillConstructs()
    {
        // Many self-hosted OpenAI-compatible servers don't authenticate at all.
        var options = BuildOptions();
        options.ApiKey = null;

        Should.NotThrow(
            () => new OpenAiCompatibleEmbeddingGenerator(new HttpClient(), options, NullLogger<OpenAiCompatibleEmbeddingGenerator>.Instance));
    }

    [Fact]
    public async Task GenerateAsync_SuccessfulResponse_ReturnsVectors()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.JsonOk("""{"data":[{"index":0,"embedding":[0.1,0.2]}]}"""));
        var generator = new OpenAiCompatibleEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(), NullLogger<OpenAiCompatibleEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync(["hello"]);

        results[0].Vector.ToArray().ShouldBe([0.1f, 0.2f]);
        results[0].Provider.ShouldBe("OpenAICompatible");
    }

    private static EmbeddingOptions BuildOptions() => new()
    {
        Provider = "OpenAICompatible",
        Model = "bge-m3",
        BaseUrl = "http://localhost:8080",
        ApiKey = "local-key",
        Dimensions = 2,
        MaxRetries = 3,
        Normalize = false,
    };
}
