using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Relation Import (spec §15): a second, fully independent pass over the same CIIR JSONL file -
/// never depends on Document Import having run or completed (spec §4/§18). Every relation in the
/// file is written under the caller-supplied project resolved once by <c>StartIndexation</c>
/// (spec's "Atualização — Identidade de projeto informada pelo chamador") - each record's own
/// <c>project</c> field is not consulted here. Batches by the number of relation rows accumulated
/// rather than by CIIR record/line count, since a single record can carry anywhere from zero to
/// hundreds of relations and the spec's batch-size intent (§30/§31) is about bounding the size of
/// each database transaction.
/// </summary>
public sealed class ImportRelations
{
    private readonly ICiirJsonlReader _reader;
    private readonly ICiirRelationWriter _relationWriter;
    private readonly IndexingOptions _options;
    private readonly ILogger<ImportRelations> _logger;

    public ImportRelations(
        ICiirJsonlReader reader,
        ICiirRelationWriter relationWriter,
        IndexingOptions options,
        ILogger<ImportRelations> logger)
    {
        _reader = reader;
        _relationWriter = relationWriter;
        _options = options;
        _logger = logger;
    }

    /// <summary>Runs Relation Import over the file at <paramref name="path"/>.</summary>
    /// <param name="path">The validated, absolute CIIR JSONL path to read.</param>
    /// <param name="runId">The current indexing run, stamped as <c>last_seen_run_id</c>.</param>
    /// <param name="projectId">Every relation in the file is upserted under this project.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    public async Task<ImportResult> ExecuteAsync(
        string path, Guid runId, long projectId, CancellationToken cancellationToken = default)
    {
        var counters = new MutableCounters();
        var pending = new List<CiirRelation>();

        await foreach (var result in _reader.ReadAsync(path, cancellationToken))
        {
            switch (result)
            {
                case CiirRecordReadResult.Success { Record.Relations.Count: 0 }:
                    break;
                case CiirRecordReadResult.Success success:
                    await AccumulateAsync(success.Record, runId, projectId, pending, counters, cancellationToken);
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

        await FlushAsync(projectId, pending, runId, cancellationToken);

        return new ImportResult { Counters = counters.ToImmutable() };
    }

    private async Task AccumulateAsync(
        CiirParsedRecord record,
        Guid runId,
        long projectId,
        List<CiirRelation> pending,
        MutableCounters counters,
        CancellationToken cancellationToken)
    {
        counters.RelationsProcessed += record.Relations.Count;
        pending.AddRange(record.Relations);

        if (pending.Count >= _options.RelationBatchSize)
        {
            await FlushAsync(projectId, pending, runId, cancellationToken);
            pending.Clear();
        }
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
