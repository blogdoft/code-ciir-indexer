using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class ImportDocumentsTests : IDisposable
{
    private const long ProjectId = 1;

    private readonly List<string> _tempFiles = [];
    private readonly JsonlCiirReader _reader = new();
    private readonly ICiirDocumentWriter _documentWriter = Substitute.For<ICiirDocumentWriter>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IEmbeddingFingerprintGenerator _fingerprintGenerator = new EmbeddingFingerprintGenerator();
    private readonly IndexingOptions _options = new();

    public ImportDocumentsTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(2);
        _embeddingGenerator.BatchSize.Returns(32);

        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?>());
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NewDocumentWithEmbeddingText_GeneratesAnEmbedding()
    {
        var textHash = Sha256Of("text-v1");
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), textHash: textHash, embeddingText: "some text"));
        StubGeneratedVector(0.1f, 0.2f);

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        result.Counters.DocumentsProcessed.ShouldBe(1);
        result.Counters.DocumentsInserted.ShouldBe(1);
        result.Counters.EmbeddingsGenerated.ShouldBe(1);
        result.Counters.EmbeddingsReused.ShouldBe(0);
        await _documentWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirDocumentUpsert>>(batch =>
                batch.Count == 1 && !batch.First().ReuseExistingEmbedding && batch.First().Embedding!.Value.Length == 2),
            Arg.Any<long>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExistingDocumentSameFingerprint_ReusesTheStoredEmbeddingWithoutCallingTheProvider()
    {
        var ciirId = Sha256Of("A");
        var textHash = Sha256Of("text-v1");
        var path = WriteJsonl(BuildDocumentLine(ciirId, textHash: textHash, embeddingText: "some text"));
        var matchingFingerprint = _fingerprintGenerator.Generate(textHash, "bge-m3", 2);
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?> { [ciirId] = matchingFingerprint });

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        result.Counters.EmbeddingsReused.ShouldBe(1);
        result.Counters.EmbeddingsGenerated.ShouldBe(0);
        result.Counters.DocumentsUpdated.ShouldBe(1);
        await _embeddingGenerator.DidNotReceive().GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _documentWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirDocumentUpsert>>(batch => batch.First().ReuseExistingEmbedding),
            Arg.Any<long>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmbeddingTextHashChanged_RegeneratesTheEmbedding()
    {
        var ciirId = Sha256Of("A");
        var path = WriteJsonl(BuildDocumentLine(ciirId, textHash: Sha256Of("text-v2"), embeddingText: "changed text"));
        var staleFingerprint = _fingerprintGenerator.Generate(Sha256Of("text-v1"), "bge-m3", 2);
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?> { [ciirId] = staleFingerprint });
        StubGeneratedVector(0.5f, 0.6f);

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        result.Counters.EmbeddingsGenerated.ShouldBe(1);
        result.Counters.EmbeddingsReused.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_EmbeddingModelChanged_RegeneratesTheEmbeddingEvenWithUnchangedTextHash()
    {
        var ciirId = Sha256Of("A");
        var textHash = Sha256Of("text-v1");
        var path = WriteJsonl(BuildDocumentLine(ciirId, textHash: textHash, embeddingText: "some text"));

        // Stored fingerprint was computed with a different model than what's currently configured.
        var staleFingerprint = _fingerprintGenerator.Generate(textHash, "old-model", 2);
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?> { [ciirId] = staleFingerprint });
        StubGeneratedVector(0.7f, 0.8f);

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        result.Counters.EmbeddingsGenerated.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_DocumentWithoutEmbeddingText_NeverCallsTheProvider()
    {
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), textHash: null, embeddingText: null));

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        result.Counters.EmbeddingsGenerated.ShouldBe(0);
        result.Counters.EmbeddingsReused.ShouldBe(0);
        await _embeddingGenerator.DidNotReceive().GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _documentWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirDocumentUpsert>>(batch => batch.First().Embedding == null && !batch.First().ReuseExistingEmbedding),
            Arg.Any<long>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWithDifferentProjectFields_AllLandUnderTheSinglePassedInProject()
    {
        // The record's own "project" field (spec's per-record CIIR contract field) must be ignored
        // for identity - every record in the file binds to the caller-supplied projectId regardless
        // (spec's "Atualização — Identidade de projeto informada pelo chamador").
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), textHash: null, embeddingText: null, projectName: "ProjectA"),
            BuildDocumentLine(Sha256Of("B"), textHash: null, embeddingText: null, projectName: "ProjectB"));

        await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        await _documentWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirDocumentUpsert>>(batch => batch.Count == 2),
            ProjectId,
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RunId_IsStampedOnEveryUpsertBatch()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), textHash: null, embeddingText: null));

        await CreateSut().ExecuteAsync(path, runId, ProjectId);

        await _documentWriter.Received(1).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirDocumentUpsert>>(), Arg.Any<long>(), runId, Arg.Any<CancellationToken>());
    }

    private static string BuildDocumentLine(
        string ciirId, string? textHash, string? embeddingText, string projectName = "MyProject")
    {
        var embeddingTextJson = embeddingText is null ? "null" : $"\"{embeddingText}\"";
        var textHashJson = textHash is null ? "null" : $"\"{textHash}\"";
        return $$"""
            {"schemaVersion":"1.0","id":"<CIIR_ID>","kind":"method","language":"csharp","project":"<PROJECT>","symbol":{"name":"Foo","qualifiedName":"NS.Type.Foo","canonicalName":"NS.Type.Foo()"},"embeddingText":<TEXT>,"embeddingTextHash":<HASH>}
            """
            .Replace("<CIIR_ID>", ciirId)
            .Replace("<PROJECT>", projectName)
            .Replace("<TEXT>", embeddingTextJson)
            .Replace("<HASH>", textHashJson);
    }

    private static string Sha256Of(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private ImportDocuments CreateSut() =>
        new(_reader, _documentWriter, _embeddingGenerator, _fingerprintGenerator, _options, NullLogger<ImportDocuments>.Instance);

    private void StubGeneratedVector(params float[] vector)
    {
        _embeddingGenerator
            .GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.ArgAt<IReadOnlyList<string>>(0);
                return (IReadOnlyList<EmbeddingResult>)texts
                    .Select(_ => new EmbeddingResult(vector, "TestProvider", "bge-m3", vector.Length))
                    .ToList();
            });
    }

    private string WriteJsonl(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        _tempFiles.Add(path);
        return path;
    }
}
