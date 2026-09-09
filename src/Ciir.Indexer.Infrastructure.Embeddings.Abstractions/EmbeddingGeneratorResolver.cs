using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions;

/// <summary>
/// Selects the <see cref="IEmbeddingGenerator"/> for the configured Embeddings:Provider by asking
/// each registered <see cref="IEmbeddingProviderFactory"/> whether it handles that name. This is
/// the only place that knows provider selection is a lookup, not a fixed list: it depends solely
/// on <see cref="IEmbeddingProviderFactory"/>, so plugging in a new provider module never requires
/// changing this class - only registering the new factory in the composition root.
/// </summary>
public sealed class EmbeddingGeneratorResolver
{
    private readonly IReadOnlyCollection<IEmbeddingProviderFactory> _factories;
    private readonly IServiceProvider _serviceProvider;

    public EmbeddingGeneratorResolver(IEnumerable<IEmbeddingProviderFactory> factories, IServiceProvider serviceProvider)
    {
        _factories = factories.ToArray();
        _serviceProvider = serviceProvider;
        KnownProviders = [.. _factories.Select(f => f.ProviderName)];
    }

    /// <summary>The provider names of every registered factory, for validation error messages and diagnostics.</summary>
    public IReadOnlyCollection<string> KnownProviders { get; }

    /// <summary>
    /// Validates <paramref name="options"/> (generic rules, then the matched provider's own rules)
    /// and constructs its <see cref="IEmbeddingGenerator"/>. Throws
    /// <see cref="ConfigurationValidationException"/> if no registered provider matches.
    /// </summary>
    /// <param name="options">The embedding configuration to resolve a generator for.</param>
    public IEmbeddingGenerator Resolve(EmbeddingOptions options)
    {
        EmbeddingOptionsValidator.Validate(options, KnownProviders);

        var factory = FindFactory(options.Provider);
        factory.Validate(options);
        return factory.Create(options, _serviceProvider);
    }

    private IEmbeddingProviderFactory FindFactory(string providerName)
    {
        var factory = _factories.FirstOrDefault(
            f => string.Equals(f.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        return factory ?? throw new ConfigurationValidationException(
            $"Unknown Embeddings:Provider '{providerName}'. Known providers: {string.Join(", ", KnownProviders)}.");
    }
}
