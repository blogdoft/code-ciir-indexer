using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Infrastructure.Embeddings.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.Embeddings.Tests;

public sealed class EmbeddingGeneratorResolverTests
{
    [Fact]
    public void Resolve_MatchingProviderCaseInsensitive_UsesThatFactory()
    {
        var ollamaFactory = BuildFactory("Ollama");
        var openAiFactory = BuildFactory("OpenAI");
        var resolver = new EmbeddingGeneratorResolver([ollamaFactory, openAiFactory], Substitute.For<IServiceProvider>());
        var options = BuildOptions("ollama");

        resolver.Resolve(options);

        ollamaFactory.Received(1).Create(options, Arg.Any<IServiceProvider>());
        openAiFactory.DidNotReceive().Create(Arg.Any<EmbeddingOptions>(), Arg.Any<IServiceProvider>());
    }

    [Fact]
    public void Resolve_UnknownProvider_ThrowsWithKnownProvidersListed()
    {
        var resolver = new EmbeddingGeneratorResolver([BuildFactory("Ollama")], Substitute.For<IServiceProvider>());
        var options = BuildOptions("DoesNotExist");

        var ex = Should.Throw<ConfigurationValidationException>(() => resolver.Resolve(options));

        ex.Message.ShouldContain("Ollama");
    }

    [Fact]
    public void Resolve_MatchedFactory_IsValidatedBeforeBeingConstructed()
    {
        var factory = BuildFactory("Ollama");
        var resolver = new EmbeddingGeneratorResolver([factory], Substitute.For<IServiceProvider>());
        var options = BuildOptions("Ollama");

        resolver.Resolve(options);

        Received.InOrder(() =>
        {
            factory.Validate(options);
            factory.Create(options, Arg.Any<IServiceProvider>());
        });
    }

    [Fact]
    public void KnownProviders_ReflectsEveryRegisteredFactory()
    {
        var resolver = new EmbeddingGeneratorResolver(
            [BuildFactory("Ollama"), BuildFactory("OpenAI")], Substitute.For<IServiceProvider>());

        resolver.KnownProviders.ShouldBe(["Ollama", "OpenAI"]);
    }

    private static IEmbeddingProviderFactory BuildFactory(string providerName)
    {
        var factory = Substitute.For<IEmbeddingProviderFactory>();
        factory.ProviderName.Returns(providerName);
        factory.Create(Arg.Any<EmbeddingOptions>(), Arg.Any<IServiceProvider>()).Returns(Substitute.For<IEmbeddingGenerator>());
        return factory;
    }

    private static EmbeddingOptions BuildOptions(string provider) => new()
    {
        Provider = provider,
        Model = "bge-m3",
        Dimensions = 1024,
        BatchSize = 32,
        TimeoutSeconds = 60,
        MaxRetries = 3,
    };
}
