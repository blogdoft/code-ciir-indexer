using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// The API's entry point (spec §2): validates the request, resolves the caller-supplied project
/// identity exactly once (spec's "Atualização — Identidade de projeto informada pelo chamador" -
/// never derived from any CIIR record's own <c>project</c> field), creates the
/// <see cref="IndexingRun"/> row, and hands it off for background execution - then returns
/// immediately without waiting for the run to finish. Contains no indexing logic of its own; the
/// controller that calls this is a pure delivery mechanism (spec §40).
/// </summary>
public sealed class StartIndexation
{
    private readonly IInputResolver _inputResolver;
    private readonly IProjectStore _projectStore;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IIndexingRunStore _runStore;
    private readonly IIndexationQueue _queue;

    public StartIndexation(
        IInputResolver inputResolver,
        IProjectStore projectStore,
        IEmbeddingGenerator embeddingGenerator,
        IIndexingRunStore runStore,
        IIndexationQueue queue)
    {
        _inputResolver = inputResolver;
        _projectStore = projectStore;
        _embeddingGenerator = embeddingGenerator;
        _runStore = runStore;
        _queue = queue;
    }

    /// <summary>Validates the request and starts a new indexing run.</summary>
    /// <param name="projectName">
    /// The caller-supplied project identity every record in the file will be bound to, required.
    /// </param>
    /// <param name="requestedPath">The raw, untrusted path from the request body.</param>
    /// <param name="gitUrl">The project's git repository URL, optional.</param>
    /// <param name="gitRawUrl">The project's git raw-content URL, optional.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    public async Task<Result<IndexingRun>> ExecuteAsync(
        string projectName,
        string requestedPath,
        string? gitUrl = null,
        string? gitRawUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return Result<IndexingRun>.FromFailure(
                new Failure("400-project-name-required", "The 'projectName' field is required."));
        }

        var pathResult = _inputResolver.ResolveAndValidate(requestedPath);
        if (pathResult.IsFailure)
        {
            return Result<IndexingRun>.FromFailure(pathResult.Failure);
        }

        var embeddingModel = new EmbeddingModel(_embeddingGenerator.Model, _embeddingGenerator.Dimensions);
        var project = await _projectStore.EnsureProjectAsync(
            projectName, gitUrl, gitRawUrl, embeddingModel, cancellationToken);

        var run = await _runStore.CreateAsync(pathResult.Value, project.Id, cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);

        return Result<IndexingRun>.FromSuccess(run);
    }
}
