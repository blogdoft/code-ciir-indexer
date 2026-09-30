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

        services.AddScoped<ImportDocuments>();
        services.AddScoped<ImportRelations>();
        services.AddScoped<ResolveRelations>();
        services.AddScoped<RunIndexation>();

        services.AddScoped<ListProjects>();
        services.AddScoped<CreateProject>();
        services.AddScoped<UpdateProject>();
        services.AddScoped<DeleteProject>();

        return services;
    }
}
