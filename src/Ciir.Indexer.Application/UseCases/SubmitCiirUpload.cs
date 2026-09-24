using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// The CIIR upload endpoint's entry point (upload spec §4): validates the request, streams the
/// file into object storage, and creates the <see cref="CiirUpload"/> row a background worker will
/// later pick up. Never creates or updates a project - <c>projectId</c> (see
/// <see cref="ExecuteAsync"/>) must already exist (upload spec §3). Contains no
/// HTTP/multipart-parsing logic of its own; the controller that calls this is a pure delivery
/// mechanism that has already isolated the file's stream.
/// </summary>
public sealed class SubmitCiirUpload
{
    private readonly IProjectStore _projectStore;
    private readonly IObjectStorage _objectStorage;
    private readonly ICiirUploadStore _uploadStore;
    private readonly string _bucket;
    private readonly UploadOptions _options;

    public SubmitCiirUpload(
        IProjectStore projectStore,
        IObjectStorage objectStorage,
        ICiirUploadStore uploadStore,
        string bucket,
        UploadOptions options)
    {
        _projectStore = projectStore;
        _objectStorage = objectStorage;
        _uploadStore = uploadStore;
        _bucket = bucket;
        _options = options;
    }

    /// <summary>Validates the request and stores the uploaded CIIR file for later processing.</summary>
    /// <param name="projectId">
    /// The raw <c>projectId</c> form field value; must be a valid id of an already-registered
    /// project.
    /// </param>
    /// <param name="fileName">The uploaded file's original name, used only to validate its extension.</param>
    /// <param name="ciirFileContent">The uploaded file's content, read forward-only exactly once.</param>
    /// <param name="contentLength">The content length in bytes, when known upfront; otherwise <c>null</c>.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    public async Task<Result<CiirUpload>> ExecuteAsync(
        string? projectId,
        string fileName,
        Stream ciirFileContent,
        long? contentLength,
        CancellationToken cancellationToken = default)
    {
        if (!CiirUploadValidation.TryParseProjectId(projectId, out var parsedProjectId, out var parseFailure))
        {
            return Result<CiirUpload>.FromFailure(parseFailure!);
        }

        var extensionFailure = CiirUploadValidation.ValidateJsonlExtension(fileName);
        if (extensionFailure is not null)
        {
            return Result<CiirUpload>.FromFailure(extensionFailure);
        }

        if (contentLength is { } length && length > _options.MaxCiirFileSizeBytes)
        {
            return Result<CiirUpload>.FromFailure(
                new Failure("413-file-too-large", $"The file exceeds the maximum allowed size of {_options.MaxCiirFileSizeBytes} bytes."));
        }

        var project = await _projectStore.GetByPublicIdAsync(parsedProjectId, cancellationToken);
        if (project is null)
        {
            return Result<CiirUpload>.FromFailure(
                new Failure("404-project-not-found", $"No project with id '{parsedProjectId}' was found."));
        }

        var objectKey = $"{Guid.NewGuid():N}/ciir.jsonl";
        await _objectStorage.UploadAsync(_bucket, objectKey, ciirFileContent, contentLength, "application/x-ndjson", cancellationToken);

        var upload = await _uploadStore.CreateAsync(project.Id, _bucket, objectKey, cancellationToken);
        return Result<CiirUpload>.FromSuccess(upload);
    }
}
