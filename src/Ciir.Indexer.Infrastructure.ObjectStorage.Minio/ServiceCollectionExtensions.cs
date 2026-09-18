using Ciir.Indexer.Application.Ports;
using global::Minio;
using Microsoft.Extensions.DependencyInjection;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.Minio;

/// <summary>Composition-root wiring for the MinIO <see cref="IObjectStorage"/> adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the MinIO client and <see cref="IObjectStorage"/> adapter.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">The MinIO connection configuration.</param>
    public static IServiceCollection AddMinioObjectStorage(this IServiceCollection services, MinioOptions options)
    {
        var client = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(options.UseSsl)
            .Build();

        services.AddSingleton(client);
        services.AddSingleton<IObjectStorage, MinioObjectStorage>();

        return services;
    }
}
