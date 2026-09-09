using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;

namespace Ciir.Indexer.Api.Indexation;

/// <summary>
/// The single background worker that drains <see cref="IndexationChannel"/> and runs each queued
/// indexation to completion, one at a time (spec §2/§33). On startup, marks any run left
/// in-progress from a previous process lifetime as failed - crash recovery, and a pure status
/// update that never touches data (spec §27).
/// </summary>
public sealed class IndexationWorker : BackgroundService
{
    private readonly IndexationChannel _channel;
    private readonly IIndexingRunStore _runStore;
    private readonly RunIndexation _runIndexation;
    private readonly ILogger<IndexationWorker> _logger;

    /// <summary>Initializes a new instance of the <see cref="IndexationWorker"/> class.</summary>
    /// <param name="channel">The queue this worker drains, one run at a time.</param>
    /// <param name="runStore">Used for startup orphaned-run reconciliation and to look up each dequeued run's path.</param>
    /// <param name="runIndexation">The orchestrator invoked for each dequeued run id.</param>
    /// <param name="logger">Logs orphaned-run reconciliation and dequeue anomalies.</param>
    public IndexationWorker(
        IndexationChannel channel, IIndexingRunStore runStore, RunIndexation runIndexation, ILogger<IndexationWorker> logger)
    {
        _channel = channel;
        _runStore = runStore;
        _runIndexation = runIndexation;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles any run left in-progress by a previous process lifetime (marking it failed, per
    /// spec §27), then processes queued run ids one at a time for the lifetime of the host.
    /// </summary>
    /// <param name="stoppingToken">Signaled when the host begins a graceful shutdown.</param>
    /// <returns>A task that completes when <paramref name="stoppingToken"/> is triggered and the current run (if any) finishes.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconciled = await _runStore.ReconcileOrphanedRunsAsync(stoppingToken);
        if (reconciled.Count > 0)
        {
            _logger.LogWarning("Marked {Count} orphaned indexing run(s) as failed on startup.", reconciled.Count);
        }

        await foreach (var runId in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var run = await _runStore.GetAsync(runId, stoppingToken);
            if (run is null)
            {
                _logger.LogWarning("Indexing run {RunId} was dequeued but no longer exists.", runId);
                continue;
            }

            await _runIndexation.ExecuteAsync(runId, run.Path, stoppingToken);
        }
    }
}
