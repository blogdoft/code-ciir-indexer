using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Document Import (spec §5): streams a CIIR JSONL file, decides per document whether a new
/// embedding is needed via the fingerprint addendum's algorithm, and upserts the result under the
/// caller-supplied project resolved once per run, before this runs (spec's "Atualização —
/// Identidade de projeto informada pelo chamador") - each record's own <c>project</c> field is not
/// consulted here. Independent of the future Relation Import use case - both read the same file on
/// their own, per spec §4.
/// </summary>
public sealed class ImportDocuments
{
    private readonly ICiirJsonlReader _reader;
    private readonly ICiirDocumentWriter _documentWriter;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IEmbeddingFingerprintGenerator _fingerprintGenerator;
    private readonly IndexingOptions _options;
    private readonly ILogger<ImportDocuments> _logger;

    public ImportDocuments(
        ICiirJsonlReader reader,
        ICiirDocumentWriter documentWriter,
        IEmbeddingGenerator embeddingGenerator,
        IEmbeddingFingerprintGenerator fingerprintGenerator,
        IndexingOptions options,
        ILogger<ImportDocuments> logger)
    {
        _reader = reader;
        _documentWriter = documentWriter;
        _embeddingGenerator = embeddingGenerator;
        _fingerprintGenerator = fingerprintGenerator;
        _options = options;
        _logger = logger;
    }

    /// <summary>Runs Document Import over the file at <paramref name="path"/>.</summary>
    /// <param name="path">The validated, absolute CIIR JSONL path to read.</param>
    /// <param name="runId">The current indexing run, stamped as <c>last_seen_run_id</c>.</param>
    /// <param name="projectId">Every document in the file is upserted under this project.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    public async Task<ImportResult> ExecuteAsync(
        string path, Guid runId, long projectId, CancellationToken cancellationToken = default)
    {
        var counters = new MutableCounters();
        var batch = new List<CiirParsedRecord>(_options.DocumentBatchSize);

        await foreach (var result in _reader.ReadAsync(path, cancellationToken))
        {
            switch (result)
            {
                case CiirRecordReadResult.Success success:
                    batch.Add(success.Record);
                    if (batch.Count >= _options.DocumentBatchSize)
                    {
                        await ProcessBatchAsync(batch, runId, projectId, counters, cancellationToken);
                        batch.Clear();
                    }

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

        if (batch.Count > 0)
        {
            await ProcessBatchAsync(batch, runId, projectId, counters, cancellationToken);
        }

        return new ImportResult { Counters = counters.ToImmutable() };
    }

    private async Task ProcessBatchAsync(
        IReadOnlyList<CiirParsedRecord> batch,
        Guid runId,
        long projectId,
        MutableCounters counters,
        CancellationToken cancellationToken)
    {
        var ciirIds = batch.Select(record => record.Document.CiirId.Value).ToArray();
        var existingFingerprints = await _documentWriter.GetExistingFingerprintsAsync(projectId, ciirIds, cancellationToken);

        var upserts = new CiirDocumentUpsert[batch.Count];
        var fingerprints = new string?[batch.Count];
        var pendingEmbeddingIndexes = new List<int>();

        for (var i = 0; i < batch.Count; i++)
        {
            var document = batch[i].Document;
            var existed = existingFingerprints.ContainsKey(document.CiirId.Value);
            counters.DocumentsProcessed++;
            if (existed)
            {
                counters.DocumentsUpdated++;
            }
            else
            {
                counters.DocumentsInserted++;
            }

            if (document.EmbeddingText is null || document.EmbeddingTextHash is null)
            {
                upserts[i] = new CiirDocumentUpsert { Document = document };
                continue;
            }

            var fingerprint = _fingerprintGenerator.Generate(
                document.EmbeddingTextHash.Value.Value, _embeddingGenerator.Model, _embeddingGenerator.Dimensions);
            fingerprints[i] = fingerprint;
            var storedFingerprint = existingFingerprints.GetValueOrDefault(document.CiirId.Value);

            if (storedFingerprint is not null && storedFingerprint == fingerprint)
            {
                upserts[i] = BuildUpsert(document, embedding: null, reuseExisting: true, fingerprint);
                counters.EmbeddingsReused++;
            }
            else
            {
                pendingEmbeddingIndexes.Add(i);
            }
        }

        if (pendingEmbeddingIndexes.Count > 0)
        {
            var texts = pendingEmbeddingIndexes.Select(index => batch[index].Document.EmbeddingText!).ToArray();
            var results = await GenerateEmbeddingsAsync(texts, cancellationToken);

            for (var j = 0; j < pendingEmbeddingIndexes.Count; j++)
            {
                var index = pendingEmbeddingIndexes[j];
                upserts[index] = BuildUpsert(batch[index].Document, results[j].Vector, reuseExisting: false, fingerprints[index]);
                counters.EmbeddingsGenerated++;
            }
        }

        await _documentWriter.UpsertBatchAsync(upserts, projectId, runId, cancellationToken);
    }

    private CiirDocumentUpsert BuildUpsert(
        CiirDocument document, ReadOnlyMemory<float>? embedding, bool reuseExisting, string? fingerprint) => new()
        {
            Document = document,
            Embedding = embedding,
            ReuseExistingEmbedding = reuseExisting,
            EmbeddingModel = _embeddingGenerator.Model,
            EmbeddingDimensions = _embeddingGenerator.Dimensions,
            EmbeddingFingerprintHash = fingerprint,
        };

    private async Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var results = new List<EmbeddingResult>(texts.Count);
        foreach (var chunk in texts.Chunk(_embeddingGenerator.BatchSize))
        {
            var chunkResults = await _embeddingGenerator.GenerateAsync(chunk, cancellationToken);
            results.AddRange(chunkResults);
        }

        return results;
    }

    private sealed class MutableCounters
    {
        public long DocumentsProcessed { get; set; }

        public long DocumentsInserted { get; set; }

        public long DocumentsUpdated { get; set; }

        public long EmbeddingsGenerated { get; set; }

        public long EmbeddingsReused { get; set; }

        public IndexingCounters ToImmutable() => new()
        {
            DocumentsProcessed = DocumentsProcessed,
            DocumentsInserted = DocumentsInserted,
            DocumentsUpdated = DocumentsUpdated,
            EmbeddingsGenerated = EmbeddingsGenerated,
            EmbeddingsReused = EmbeddingsReused,
        };
    }
}
