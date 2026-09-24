using BlogDoFT.Libs.WarmUp;
using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Api.WarmUp;

/// <summary>
/// Recovers from a crash mid-run (spec §27): marks any indexing run left in-progress by a
/// previous process lifetime as failed. A one-shot startup check, not an ongoing background loop -
/// every run is now started synchronously within <c>CiirUploadWorker</c>'s own polling cycle
/// (<c>ProcessNextCiirUpload</c> -&gt; <c>RunIndexation</c>), so there is no separate queue/worker
/// to recover on. Registered after <see cref="Ciir.Indexer.Infrastructure.PostgreSql.Migrations.DatabaseMigrator"/>
/// so the warm-up host runs migrations first - the <c>indexing_runs</c> table must already exist
/// before this queries it.
/// </summary>
public sealed class OrphanedIndexingRunReconciler : IWarmUpCommand
{
    private readonly IIndexingRunStore _runStore;
    private readonly ILogger<OrphanedIndexingRunReconciler> _logger;

    /// <summary>Initializes a new instance of the <see cref="OrphanedIndexingRunReconciler"/> class.</summary>
    /// <param name="runStore">The port used to find and mark orphaned runs.</param>
    /// <param name="logger">Logs how many orphaned runs, if any, were reconciled.</param>
    public OrphanedIndexingRunReconciler(IIndexingRunStore runStore, ILogger<OrphanedIndexingRunReconciler> logger)
    {
        _runStore = runStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Execute()
    {
        var reconciledRunIds = await _runStore.ReconcileOrphanedRunsAsync();
        if (reconciledRunIds.Count > 0)
        {
            _logger.LogWarning("Marked {Count} orphaned indexing run(s) as failed on startup.", reconciledRunIds.Count);
        }
    }
}
