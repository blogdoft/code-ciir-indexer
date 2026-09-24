using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Registers a CIIR file the caller has already placed directly in object storage (e.g. via
/// <c>mc cp</c> or another S3-compatible client) - the entry point for files too large to push
/// through <see cref="SubmitCiirUpload"/>'s own HTTP request body. Creates the same
/// <see cref="CiirUpload"/> row <see cref="SubmitCiirUpload"/> does, so the existing background
/// worker (<see cref="ProcessNextCiirUpload"/>) picks it up exactly the same way - the only
/// difference between the two use cases is who wrote the object's bytes.
/// </summary>
public sealed class RegisterCiirUpload
{
    private readonly IProjectStore _projectStore;
    private readonly IObjectStorage _objectStorage;
    private readonly ICiirUploadStore _uploadStore;
    private readonly string _bucket;

    public RegisterCiirUpload(
        IProjectStore projectStore, IObjectStorage objectStorage, ICiirUploadStore uploadStore, string bucket)
    {
        _projectStore = projectStore;
        _objectStorage = objectStorage;
        _uploadStore = uploadStore;
        _bucket = bucket;
    }

    /// <summary>Validates the request and registers an already-uploaded CIIR file for later processing.</summary>
    /// <param name="projectId">
    /// The raw <c>projectId</c> field value; must be a valid id of an already-registered project.
    /// </param>
    /// <param name="objectKey">
    /// The key the file was already stored under in this service's configured upload bucket - the
    /// caller must have uploaded it there themselves (directly to MinIO) before calling this.
    /// </param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    public async Task<Result<CiirUpload>> ExecuteAsync(
        string? projectId, string? objectKey, CancellationToken cancellationToken = default)
    {
        if (!CiirUploadValidation.TryParseProjectId(projectId, out var parsedProjectId, out var parseFailure))
        {
            return Result<CiirUpload>.FromFailure(parseFailure!);
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Result<CiirUpload>.FromFailure(
                new Failure("400-object-key-required", "The 'objectKey' field is required."));
        }

        var extensionFailure = CiirUploadValidation.ValidateJsonlExtension(objectKey);
        if (extensionFailure is not null)
        {
            return Result<CiirUpload>.FromFailure(extensionFailure);
        }

        var project = await _projectStore.GetByPublicIdAsync(parsedProjectId, cancellationToken);
        if (project is null)
        {
            return Result<CiirUpload>.FromFailure(
                new Failure("404-project-not-found", $"No project with id '{parsedProjectId}' was found."));
        }

        var exists = await _objectStorage.ExistsAsync(_bucket, objectKey, cancellationToken);
        if (!exists)
        {
            return Result<CiirUpload>.FromFailure(new Failure(
                "404-object-not-found",
                $"No object exists at key '{objectKey}' in bucket '{_bucket}'. Upload the file there before registering it."));
        }

        var upload = await _uploadStore.CreateAsync(project.Id, _bucket, objectKey, cancellationToken);
        return Result<CiirUpload>.FromSuccess(upload);
    }
}
