using Amazon.Runtime;
using Amazon.S3;
using Ciir.Indexer.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.S3;

/// <summary>Composition-root wiring for the S3-compatible <see cref="IObjectStorage"/> adapter.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the S3 client and <see cref="IObjectStorage"/> adapter.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">The object storage connection configuration.</param>
    public static IServiceCollection AddS3ObjectStorage(this IServiceCollection services, S3Options options)
    {
        var scheme = options.UseSsl ? "https" : "http";
        var config = new AmazonS3Config
        {
            ServiceURL = $"{scheme}://{options.Endpoint}",
            AuthenticationRegion = options.Region,

            // Garage only resolves virtual-hosted-style buckets under its configured root_domain, so
            // path-style is the form that works against any endpoint (Service DNS, Ingress, localhost).
            ForcePathStyle = true,

            // AWSSDK.S3 v4 adds CRC32 checksums to every request by default; S3-compatible servers
            // may not support them, so only compute/validate them when the operation requires it.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        services.AddSingleton<IAmazonS3>(new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey), config));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        return services;
    }
}
