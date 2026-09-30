using Ciir.Indexer.Application;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Infrastructure.ObjectStorage.S3;

namespace Ciir.Indexer.Api.Uploads;

/// <summary>
/// Composition-root wiring for the CIIR upload feature (upload spec §4/§8): the bucket name is
/// read from the "ObjectStorage" section here, in the composition root, rather than threaded through the
/// Application layer, which must not depend on an infrastructure-specific options type. Uploading
/// (<c>POST /api/ciir-uploads</c>) and registering an already-uploaded file
/// (<c>POST /api/ciir-uploads/register</c>) are the only two ways a CIIR file reaches indexation -
/// there is no local-filesystem-path entry point.
/// </summary>
public static class CiirUploadsServiceCollectionExtensions
{
    /// <summary>Registers the CIIR upload use cases, options, and the background worker that drives them.</summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <param name="objectStorageOptions">The configured object storage bucket the upload use cases read/write against.</param>
    /// <param name="uploadOptions">The configured upload size/concurrency limits.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, so calls can be chained.</returns>
    public static IServiceCollection AddCiirUploadsFeature(
        this IServiceCollection services, S3Options objectStorageOptions, UploadOptions uploadOptions)
    {
        services.AddSingleton(uploadOptions);
        services.AddScoped(sp => new SubmitCiirUpload(
            sp.GetRequiredService<IProjectStore>(),
            sp.GetRequiredService<IObjectStorage>(),
            sp.GetRequiredService<ICiirUploadStore>(),
            objectStorageOptions.BucketName,
            uploadOptions));
        services.AddScoped(sp => new RegisterCiirUpload(
            sp.GetRequiredService<IProjectStore>(),
            sp.GetRequiredService<IObjectStorage>(),
            sp.GetRequiredService<ICiirUploadStore>(),
            objectStorageOptions.BucketName));
        services.AddScoped(sp => new ProcessNextCiirUpload(
            sp.GetRequiredService<ICiirUploadStore>(),
            sp.GetRequiredService<IObjectStorage>(),
            sp.GetRequiredService<IProjectStore>(),
            sp.GetRequiredService<IIndexingRunStore>(),
            sp.GetRequiredService<RunIndexation>(),
            uploadOptions,
            sp.GetRequiredService<ILogger<ProcessNextCiirUpload>>()));
        services.AddHostedService<CiirUploadWorker>();

        return services;
    }
}
