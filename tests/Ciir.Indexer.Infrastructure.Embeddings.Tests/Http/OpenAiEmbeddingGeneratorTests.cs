using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.OpenAI.OpenAI;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Net;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests.Http;

public sealed class OpenAiEmbeddingGeneratorTests
{
    [Fact]
    public void Constructor_NoApiKeyAndNoEnvironmentVariable_StillConstructs()
    {
        // Missing-API-key validation belongs to OpenAiEmbeddingProviderFactory.Validate (spec §34's
        // provider-specific-validation seam), not the generator constructor itself.
        var options = BuildOptions();

        Should.NotThrow(() => new OpenAiEmbeddingGenerator(new HttpClient(), options, NullLogger<OpenAiEmbeddingGenerator>.Instance));
    }

    [Fact]
    public async Task GenerateAsync_SuccessfulResponse_ReturnsVectorsByIndexRegardlessOfResponseOrder()
    {
        var handler = new StubHttpMessageHandler(() => StubHttpMessageHandler.JsonOk(
            """{"data":[{"index":1,"embedding":[0.3,0.4]},{"index":0,"embedding":[0.1,0.2]}]}"""));
        var generator = new OpenAiEmbeddingGenerator(new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OpenAiEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync(["hello", "world"]);

        results[0].Vector.ToArray().ShouldBe([0.1f, 0.2f]);
        results[1].Vector.ToArray().ShouldBe([0.3f, 0.4f]);
    }

    [Fact]
    public async Task GenerateAsync_DimensionMismatch_Throws()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.JsonOk("""{"data":[{"index":0,"embedding":[0.1,0.2,0.3]}]}"""));
        var generator = new OpenAiEmbeddingGenerator(new HttpClient(handler), BuildOptions(dimensions: 1536), NullLogger<OpenAiEmbeddingGenerator>.Instance);

        await Should.ThrowAsync<EmbeddingDimensionMismatchException>(() => generator.GenerateAsync(["hello"]));
    }

    [Fact]
    public async Task GenerateAsync_TransientFailureThenSuccess_Retries()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.Status(HttpStatusCode.TooManyRequests),
            () => StubHttpMessageHandler.JsonOk("""{"data":[{"index":0,"embedding":[1,2]}]}"""));
        var generator = new OpenAiEmbeddingGenerator(new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OpenAiEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync(["hello"]);

        handler.RequestCount.ShouldBe(2);
        results[0].Vector.ToArray().ShouldBe([1f, 2f]);
    }

    [Fact]
    public async Task GenerateAsync_PermanentAuthError_DoesNotRetry()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided"}}"""));
        var generator = new OpenAiEmbeddingGenerator(new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OpenAiEmbeddingGenerator>.Instance);

        var ex = await Should.ThrowAsync<EmbeddingGenerationException>(() => generator.GenerateAsync(["hello"]));

        handler.RequestCount.ShouldBe(1);
        ex.Message.ShouldContain("Incorrect API key");
    }

    private static EmbeddingOptions BuildOptions(int dimensions = 1536, int maxRetries = 3) => new()
    {
        Provider = "OpenAI",
        Model = "text-embedding-3-small",
        ApiKey = "sk-test",
        Dimensions = dimensions,
        MaxRetries = maxRetries,
        Normalize = false,
    };
}
