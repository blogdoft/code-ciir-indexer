using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Drains the durable <see cref="ICiirUploadStore"/> queue one upload at a time (upload spec §8):
/// reclaims permanently-failed stuck uploads, claims the oldest eligible one, downloads it from
/// object storage, and reuses <see cref="RunIndexation"/> - the same indexation engine the
/// local-path flow uses - rather than duplicating any embedding/relation logic. Called in a loop by
/// a <c>BackgroundService</c>; every call processes at most one upload and reports whether it did,
/// so the caller knows whether to poll again immediately or wait.
/// </summary>
public sealed class ProcessNextCiirUpload
{
    private readonly ICiirUploadStore _uploadStore;
    private readonly IObjectStorage _objectStorage;
    private readonly IProjectStore _projectStore;
    private readonly IIndexingRunStore _runStore;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RunIndexation _runIndexation;
    private readonly UploadOptions _options;
    private readonly ILogger<ProcessNextCiirUpload> _logger;

    public ProcessNextCiirUpload(
        ICiirUploadStore uploadStore,
        IObjectStorage objectStorage,
        IProjectStore projectStore,
        IIndexingRunStore runStore,
        IEmbeddingGenerator embeddingGenerator,
        RunIndexation runIndexation,
        UploadOptions options,
        ILogger<ProcessNextCiirUpload> logger)
    {
        _uploadStore = uploadStore;
        _objectStorage = objectStorage;
        _projectStore = projectStore;
        _runStore = runStore;
        _embeddingGenerator = embeddingGenerator;
        _runIndexation = runIndexation;
        _options = options;
        _logger = logger;
    }

    /// <summary>Reclaims exhausted stuck uploads, then claims and processes at most one upload.</summary>
    /// <param name="cancellationToken">Propagates shutdown cancellation.</param>
    /// <returns><c>true</c> if an upload was claimed and processed; <c>false</c> if nothing was eligible.</returns>
    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var stuckTimeout = TimeSpan.FromMinutes(_options.StuckProcessingTimeoutMinutes);

        var exhausted = await _uploadStore.ReclaimExhaustedAsync(stuckTimeout, _options.MaxRetryCount, cancellationToken);
        foreach (var upload in exhausted)
        {
            _logger.LogWarning(
                "Upload {UploadId} exhausted its retry attempts while stuck in processing; deleting its object.", upload.Id);
            await _objectStorage.DeleteAsync(upload.Bucket, upload.ObjectKey, cancellationToken);
        }

        var claimed = await _uploadStore.ClaimNextAsync(stuckTimeout, _options.MaxRetryCount, cancellationToken);
        if (claimed is null)
        {
            return false;
        }

        await ProcessAsync(claimed, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(CiirUpload upload, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetByIdAsync(upload.ProjectId, cancellationToken);
        if (project is null)
        {
            _logger.LogWarning(
                "Upload {UploadId} references project {ProjectId}, which no longer exists.", upload.Id, upload.ProjectId);
            await _objectStorage.DeleteAsync(upload.Bucket, upload.ObjectKey, cancellationToken);
            await _uploadStore.MarkFailedAsync(
                upload.Id, $"Project '{upload.ProjectId}' no longer exists.", cancellationToken);
            return;
        }

        var stagingPath = Path.Combine(_options.StagingDirectory, upload.Id.ToString("N"), "ciir.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);

        try
        {
            await _objectStorage.DownloadToFileAsync(upload.Bucket, upload.ObjectKey, stagingPath, cancellationToken);

            // Refreshes the project's stored embedding model/dimensions if the configured provider
            // changed since it was registered (spec §55) - never creates a new project, since the
            // name already exists.
            var embeddingModel = new EmbeddingModel(_embeddingGenerator.Model, _embeddingGenerator.Dimensions);
            var refreshedProject = await _projectStore.EnsureProjectAsync(
                project.Name, project.GitUrl, project.GitRawUrl, embeddingModel, cancellationToken);

            var run = await _runStore.CreateAsync(stagingPath, refreshedProject.Id, cancellationToken);
            await _uploadStore.MarkIndexingRunAsync(upload.Id, run.Id, cancellationToken);

            await _runIndexation.ExecuteAsync(run.Id, stagingPath, refreshedProject.Id, cancellationToken);

            var finished = await _runStore.GetAsync(run.Id, cancellationToken);
            await _objectStorage.DeleteAsync(upload.Bucket, upload.ObjectKey, cancellationToken);

            if (finished?.Status == IndexingStatus.Completed)
            {
                await _uploadStore.MarkProcessedAsync(upload.Id, cancellationToken);
            }
            else
            {
                // A deterministic indexation failure/cancellation - never retried, since running the
                // same content again would fail the same way (upload spec §7).
                await _uploadStore.MarkFailedAsync(
                    upload.Id, finished?.Error ?? "Indexing did not complete successfully.", cancellationToken);
            }
        }
        finally
        {
            DeleteStagingDirectory(stagingPath);
        }
    }

    private void DeleteStagingDirectory(string stagingPath)
    {
        var directory = Path.GetDirectoryName(stagingPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete staging directory {Directory}.", directory);
        }
    }
}
