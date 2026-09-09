using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// The API's entry point (spec §2): validates the requested path, creates the
/// <see cref="IndexingRun"/> row, and hands it off for background execution - then returns
/// immediately without waiting for the run to finish. Contains no indexing logic of its own; the
/// controller that calls this is a pure delivery mechanism (spec §40).
/// </summary>
public sealed class StartIndexation
{
    private readonly IInputResolver _inputResolver;
    private readonly IIndexingRunStore _runStore;
    private readonly IIndexationQueue _queue;

    public StartIndexation(IInputResolver inputResolver, IIndexingRunStore runStore, IIndexationQueue queue)
    {
        _inputResolver = inputResolver;
        _runStore = runStore;
        _queue = queue;
    }

    /// <summary>Validates the request and starts a new indexing run.</summary>
    /// <param name="requestedPath">The raw, untrusted path from the request body.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    public async Task<Result<IndexingRun>> ExecuteAsync(string requestedPath, CancellationToken cancellationToken = default)
    {
        var pathResult = _inputResolver.ResolveAndValidate(requestedPath);
        if (pathResult.IsFailure)
        {
            return Result<IndexingRun>.FromFailure(pathResult.Failure);
        }

        var run = await _runStore.CreateAsync(pathResult.Value, cancellationToken);
        await _queue.EnqueueAsync(run.Id, cancellationToken);

        return Result<IndexingRun>.FromSuccess(run);
    }
}
