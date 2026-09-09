using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using Ciir.Indexer.Infrastructure.Embeddings.Ollama.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using System.Net;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests.Http;

public sealed class OllamaEmbeddingGeneratorTests
{
    [Fact]
    public void Constructor_MissingBaseUrl_Throws()
    {
        var options = BuildOptions(dimensions: 2);
        options.BaseUrl = null;

        Should.Throw<ConfigurationValidationException>(
            () => new OllamaEmbeddingGenerator(new HttpClient(), options, NullLogger<OllamaEmbeddingGenerator>.Instance));
    }

    [Fact]
    public async Task GenerateAsync_SuccessfulResponse_ReturnsVectorsPositionally()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.JsonOk(ResponseWithEmbeddings([0.1f, 0.2f], [0.3f, 0.4f])));
        var generator = new OllamaEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OllamaEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync(["hello", "world"]);

        results.Count.ShouldBe(2);
        results[0].Vector.ToArray().ShouldBe([0.1f, 0.2f]);
        results[1].Vector.ToArray().ShouldBe([0.3f, 0.4f]);
    }

    [Fact]
    public async Task GenerateAsync_DimensionMismatch_Throws()
    {
        // Mandatory spec test: an embedding whose dimension doesn't match the configured
        // dimensionality must fail loudly, never be silently truncated/padded/accepted.
        var handler = new StubHttpMessageHandler(() => StubHttpMessageHandler.JsonOk(ResponseWithEmbeddings([0.1f, 0.2f, 0.3f])));
        var generator = new OllamaEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(dimensions: 1024), NullLogger<OllamaEmbeddingGenerator>.Instance);

        await Should.ThrowAsync<EmbeddingDimensionMismatchException>(() => generator.GenerateAsync(["hello"]));
    }

    [Fact]
    public async Task GenerateAsync_TransientFailureThenSuccess_Retries()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable),
            () => StubHttpMessageHandler.JsonOk(ResponseWithEmbeddings([1f, 2f])));
        var generator = new OllamaEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OllamaEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync(["hello"]);

        handler.RequestCount.ShouldBe(2);
        results[0].Vector.ToArray().ShouldBe([1f, 2f]);
    }

    [Fact]
    public async Task GenerateAsync_PermanentClientError_DoesNotRetry()
    {
        var handler = new StubHttpMessageHandler(
            () => StubHttpMessageHandler.Status(HttpStatusCode.NotFound, """{"error":"model 'bge-m3' not found"}"""));
        var generator = new OllamaEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OllamaEmbeddingGenerator>.Instance);

        var ex = await Should.ThrowAsync<EmbeddingGenerationException>(() => generator.GenerateAsync(["hello"]));

        handler.RequestCount.ShouldBe(1);
        ex.Message.ShouldContain("not found");
    }

    [Fact]
    public async Task GenerateAsync_EmptyTexts_ReturnsEmptyWithoutCallingTheProvider()
    {
        var handler = new StubHttpMessageHandler();
        var generator = new OllamaEmbeddingGenerator(
            new HttpClient(handler), BuildOptions(dimensions: 2), NullLogger<OllamaEmbeddingGenerator>.Instance);

        var results = await generator.GenerateAsync([]);

        results.ShouldBeEmpty();
        handler.RequestCount.ShouldBe(0);
    }

    private static EmbeddingOptions BuildOptions(int dimensions, int maxRetries = 3) => new()
    {
        Provider = "Ollama",
        Model = "bge-m3",
        BaseUrl = "http://localhost:11434",
        Dimensions = dimensions,
        MaxRetries = maxRetries,
        Normalize = false,
    };

    private static string ResponseWithEmbeddings(params float[][] vectors) =>
        $$"""{"model":"bge-m3","embeddings":[{{string.Join(',', vectors.Select(v => $"[{string.Join(',', v)}]"))}}]}""";
}
