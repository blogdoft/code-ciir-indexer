using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Relation Import (spec §15): a second, fully independent pass over the same CIIR JSONL file -
/// never depends on Document Import having run or completed (spec §4/§18). Batches by the number
/// of relation rows accumulated per project rather than by CIIR record/line count, since a single
/// record can carry anywhere from zero to hundreds of relations and the spec's batch-size intent
/// (§30/§31) is about bounding the size of each database transaction.
/// </summary>
public sealed class ImportRelations
{
    private readonly ICiirJsonlReader _reader;
    private readonly IProjectStore _projectStore;
    private readonly ICiirRelationWriter _relationWriter;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IndexingOptions _options;
    private readonly ILogger<ImportRelations> _logger;

    public ImportRelations(
        ICiirJsonlReader reader,
        IProjectStore projectStore,
        ICiirRelationWriter relationWriter,
        IEmbeddingGenerator embeddingGenerator,
        IndexingOptions options,
        ILogger<ImportRelations> logger)
    {
        _reader = reader;
        _projectStore = projectStore;
        _relationWriter = relationWriter;
        _embeddingGenerator = embeddingGenerator;
        _options = options;
        _logger = logger;
    }

    /// <summary>Runs Relation Import over the file at <paramref name="path"/>.</summary>
    /// <param name="path">The validated, absolute CIIR JSONL path to read.</param>
    /// <param name="runId">The current indexing run, stamped as <c>last_seen_run_id</c>.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    public async Task<ImportResult> ExecuteAsync(
        string path, Guid runId, CancellationToken cancellationToken = default)
    {
        var counters = new MutableCounters();
        var projectCache = new Dictionary<string, Project>(StringComparer.Ordinal);
        var pendingByProjectId = new Dictionary<long, List<CiirRelation>>();

        await foreach (var result in _reader.ReadAsync(path, cancellationToken))
        {
            switch (result)
            {
                case CiirRecordReadResult.Success { Record.Relations.Count: 0 }:
                    break;
                case CiirRecordReadResult.Success success:
                    await AccumulateAsync(success.Record, runId, projectCache, pendingByProjectId, counters, cancellationToken);
                    break;
                case CiirRecordReadResult.Error error:
                    _logger.LogWarning(
                        "Invalid CIIR record at line {LineNumber} ({Category}): {Message}",
                        error.LineNumber,
                        error.Category,
                        error.Message);
                    break;
            }
        }

        foreach (var (projectId, pending) in pendingByProjectId)
        {
            await FlushAsync(projectId, pending, runId, cancellationToken);
        }

        return new ImportResult
        {
            Counters = counters.ToImmutable(),
            ProjectIds = pendingByProjectId.Keys.ToHashSet(),
        };
    }

    private async Task AccumulateAsync(
        CiirParsedRecord record,
        Guid runId,
        Dictionary<string, Project> projectCache,
        Dictionary<long, List<CiirRelation>> pendingByProjectId,
        MutableCounters counters,
        CancellationToken cancellationToken)
    {
        var project = await ResolveProjectAsync(record.ProjectName, projectCache, cancellationToken);
        counters.RelationsProcessed += record.Relations.Count;

        if (!pendingByProjectId.TryGetValue(project.Id, out var pending))
        {
            pending = [];
            pendingByProjectId[project.Id] = pending;
        }

        pending.AddRange(record.Relations);

        if (pending.Count >= _options.RelationBatchSize)
        {
            await FlushAsync(project.Id, pending, runId, cancellationToken);
            pending.Clear();
        }
    }

    private async Task<Project> ResolveProjectAsync(
        string projectName, Dictionary<string, Project> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(projectName, out var cached))
        {
            return cached;
        }

        var embeddingModel = new EmbeddingModel(_embeddingGenerator.Model, _embeddingGenerator.Dimensions);
        var project = await _projectStore.EnsureProjectAsync(projectName, embeddingModel, cancellationToken);
        cache[projectName] = project;
        return project;
    }

    private async Task FlushAsync(
        long projectId, IReadOnlyCollection<CiirRelation> pending, Guid runId, CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        await _relationWriter.UpsertBatchAsync(pending, projectId, runId, cancellationToken);
    }

    private sealed class MutableCounters
    {
        public long RelationsProcessed { get; set; }

        public IndexingCounters ToImmutable() => new()
        {
            RelationsProcessed = RelationsProcessed,
        };
    }
}
