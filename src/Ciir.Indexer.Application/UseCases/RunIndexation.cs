using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Orchestrates one full indexation, following spec §29's exact order: run Document Import and
/// Relation Import concurrently (both independent, per spec §4), resolve relations for every
/// project touched, and - only on full success - remove stale relations then stale documents before
/// marking the run completed. Any exception before that point marks the run failed instead; the
/// stale-cleanup steps (spec §27/§28) are never reached on partial failure, so previously valid data
/// is never touched by a run that didn't finish cleanly.
/// </summary>
public sealed class RunIndexation
{
    private readonly ImportDocuments _importDocuments;
    private readonly ImportRelations _importRelations;
    private readonly ResolveRelations _resolveRelations;
    private readonly ICiirDocumentWriter _documentWriter;
    private readonly ICiirRelationWriter _relationWriter;
    private readonly IIndexingRunStore _runStore;
    private readonly ILogger<RunIndexation> _logger;

    public RunIndexation(
        ImportDocuments importDocuments,
        ImportRelations importRelations,
        ResolveRelations resolveRelations,
        ICiirDocumentWriter documentWriter,
        ICiirRelationWriter relationWriter,
        IIndexingRunStore runStore,
        ILogger<RunIndexation> logger)
    {
        _importDocuments = importDocuments;
        _importRelations = importRelations;
        _resolveRelations = resolveRelations;
        _documentWriter = documentWriter;
        _relationWriter = relationWriter;
        _runStore = runStore;
        _logger = logger;
    }

    /// <summary>
    /// Runs the already-created <paramref name="runId"/> to completion. Assumes the run row exists
    /// (created by <c>ProcessNextCiirUpload</c>) with status <see cref="IndexingStatus.Pending"/>.
    /// </summary>
    /// <param name="runId">The run to execute.</param>
    /// <param name="path">The staged, absolute CIIR JSONL path to index.</param>
    /// <param name="projectId">
    /// The project every record in the file is bound to, already resolved from the caller-supplied
    /// request before this run was created - never derived from any record's own <c>project</c>
    /// field.
    /// </param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    public async Task ExecuteAsync(Guid runId, string path, long projectId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _runStore.MarkStatusAsync(runId, IndexingStatus.Running, cancellationToken: cancellationToken);

            var documentsTask = _importDocuments.ExecuteAsync(path, runId, projectId, cancellationToken);
            var relationsTask = _importRelations.ExecuteAsync(path, runId, projectId, cancellationToken);
            await Task.WhenAll(documentsTask, relationsTask);
            var documentsResult = await documentsTask;
            var relationsResult = await relationsTask;

            await _runStore.MarkStatusAsync(runId, IndexingStatus.ResolvingRelations, cancellationToken: cancellationToken);
            var resolutionCounters = await _resolveRelations.ExecuteAsync([projectId], cancellationToken);

            var counters = Merge(documentsResult.Counters, relationsResult.Counters, resolutionCounters);
            await _runStore.UpdateCountersAsync(runId, counters, cancellationToken);

            await _relationWriter.DeleteStaleAsync(projectId, runId, cancellationToken);
            await _documentWriter.DeleteStaleAsync(projectId, runId, cancellationToken);

            await _runStore.MarkStatusAsync(runId, IndexingStatus.Completed, cancellationToken: cancellationToken);

            stopwatch.Stop();
            LogCompletion(runId, stopwatch.Elapsed, counters);
        }
        catch (OperationCanceledException ex)
        {
            // Never rethrown: this method's contract is to always leave the run in a terminal,
            // queryable status. The caller is a fire-and-forget background worker (spec §33) with
            // no synchronous listener to propagate the exception to - GET /api/indexations/{id}
            // is how callers observe the outcome, not a thrown exception.
            _logger.LogWarning(ex, "Indexing run {RunId} was cancelled.", runId);
            await _runStore.MarkStatusAsync(runId, IndexingStatus.Cancelled, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing run {RunId} failed.", runId);
            await _runStore.MarkStatusAsync(runId, IndexingStatus.Failed, ex.Message, CancellationToken.None);
        }
    }

    // RelationsResolved/Unresolved map onto the resolver's richer breakdown (spec §47):
    // "resolved" = edges an internal target_document_id was actually filled in for (the
    // call-graph-traversable count); "unresolved" = relations CIIR itself classified as
    // resolution_status='unresolved' - external/dynamic targets are not failures (spec §46) and are
    // intentionally excluded from this simple persisted counter.
    private static IndexingCounters Merge(
        IndexingCounters documents, IndexingCounters relations, RelationResolutionCounters resolution) => new()
        {
            DocumentsProcessed = documents.DocumentsProcessed,
            DocumentsInserted = documents.DocumentsInserted,
            DocumentsUpdated = documents.DocumentsUpdated,
            EmbeddingsGenerated = documents.EmbeddingsGenerated,
            EmbeddingsReused = documents.EmbeddingsReused,
            RelationsProcessed = relations.RelationsProcessed,
            RelationsResolved = resolution.TargetResolved,
            RelationsUnresolved = resolution.UnresolvedTargets,
        };

    // Spec §50's throughput/duration metrics, logged once at the natural aggregation point rather
    // than scattered across every sub-step: the per-phase counters (documents/relations/embeddings)
    // are already persisted on the run row for GET /api/indexations/{id} to expose, so this line's
    // job is specifically the numbers that aren't - wall-clock duration and derived records/sec.
    private void LogCompletion(Guid runId, TimeSpan elapsed, IndexingCounters counters)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var documentsPerSecond = counters.DocumentsProcessed / seconds;
        var relationsPerSecond = counters.RelationsProcessed / seconds;

        _logger.LogInformation(
            "Indexing run {RunId} completed in {ElapsedMs}ms " +
            "({DocumentsProcessed} documents, {DocumentsPerSecond:F1}/s, " +
            "{EmbeddingsGenerated} embeddings generated, {EmbeddingsReused} reused, " +
            "{RelationsProcessed} relations, {RelationsPerSecond:F1}/s, " +
            "{RelationsResolved} resolved, {RelationsUnresolved} unresolved).",
            runId,
            elapsed.TotalMilliseconds,
            counters.DocumentsProcessed,
            documentsPerSecond,
            counters.EmbeddingsGenerated,
            counters.EmbeddingsReused,
            counters.RelationsProcessed,
            relationsPerSecond,
            counters.RelationsResolved,
            counters.RelationsUnresolved);
    }
}
