using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Ciir.Indexer.Application;

/// <summary>Composition-root wiring for the Application layer: use cases and the pure Core algorithms they depend on.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCiirIndexerApplication(this IServiceCollection services, IndexingOptions options)
    {
        services.AddSingleton(options);

        services.AddSingleton<IEmbeddingFingerprintGenerator, EmbeddingFingerprintGenerator>();
        services.AddSingleton<ICiirJsonlReader, JsonlCiirReader>();

        services.AddSingleton<StartIndexation>();
        services.AddSingleton<ImportDocuments>();
        services.AddSingleton<ImportRelations>();
        services.AddSingleton<ResolveRelations>();
        services.AddSingleton<RunIndexation>();

        return services;
    }
}
