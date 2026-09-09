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

public sealed class ImportRelationsTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly JsonlCiirReader _reader = new();
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly ICiirRelationWriter _relationWriter = Substitute.For<ICiirRelationWriter>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IndexingOptions _options = new();
    private long _nextProjectId = 1;

    public ImportRelationsTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(2);

        _projectStore
            .EnsureProjectAsync(Arg.Any<string>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new Project
            {
                Id = _nextProjectId++,
                Name = callInfo.ArgAt<string>(0),
                EmbeddingModel = callInfo.ArgAt<EmbeddingModel>(1),
            });
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RecordWithRelations_UpsertsThemAndCountsThemAsProcessed()
    {
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 2));

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid());

        result.Counters.RelationsProcessed.ShouldBe(2);
        await _relationWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirRelation>>(batch => batch.Count == 2),
            Arg.Any<long>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordWithNoRelations_NeverCallsTheWriter()
    {
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 0));

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid());

        result.Counters.RelationsProcessed.ShouldBe(0);
        await _relationWriter.DidNotReceive().UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PendingCountReachesBatchSize_FlushesBeforeEndOfFile()
    {
        _options.RelationBatchSize = 2;
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), relationCount: 2),
            BuildDocumentLine(Sha256Of("B"), relationCount: 1));

        await CreateSut().ExecuteAsync(path, Guid.NewGuid());

        // First flush at the batch-size threshold (2 relations from A), second flush for the
        // remaining 1 relation from B at end-of-file.
        await _relationWriter.Received(2).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFromDifferentProjects_AreFlushedSeparatelyPerProject()
    {
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), relationCount: 1, projectName: "ProjectA"),
            BuildDocumentLine(Sha256Of("B"), relationCount: 1, projectName: "ProjectB"));

        await CreateSut().ExecuteAsync(path, Guid.NewGuid());

        await _projectStore.Received(1).EnsureProjectAsync("ProjectA", Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>());
        await _projectStore.Received(1).EnsureProjectAsync("ProjectB", Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>());
        await _relationWriter.Received(2).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RunId_IsStampedOnEveryUpsertBatch()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));

        await CreateSut().ExecuteAsync(path, runId);

        await _relationWriter.Received(1).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), runId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MalformedLine_IsSkippedAndSubsequentValidRecordsAreStillImported()
    {
        var validLine = BuildDocumentLine(Sha256Of("A"), relationCount: 1);
        var path = WriteJsonl("{ not valid json", validLine);

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid());

        result.Counters.RelationsProcessed.ShouldBe(1);
    }

    private static string BuildDocumentLine(string ciirId, int relationCount, string projectName = "MyProject")
    {
        var relations = string.Join(
            ',',
            Enumerable.Range(0, relationCount).Select(i =>
                """{"kind":"calls","target":{"symbol":"NS.Other.Member<I>"},"resolution":{"status":"resolved","origin":"project"}}"""
                    .Replace("<I>", i.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        return $$"""
            {"schemaVersion":"1.0","id":"<CIIR_ID>","kind":"method","language":"csharp","project":"<PROJECT>","symbol":{"name":"Foo","qualifiedName":"NS.Type.Foo","canonicalName":"NS.Type.Foo()"},"relations":[<RELATIONS>]}
            """
            .Replace("<CIIR_ID>", ciirId)
            .Replace("<PROJECT>", projectName)
            .Replace("<RELATIONS>", relations);
    }

    private static string Sha256Of(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private ImportRelations CreateSut() =>
        new(_reader, _projectStore, _relationWriter, _embeddingGenerator, _options, NullLogger<ImportRelations>.Instance);

    private string WriteJsonl(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        _tempFiles.Add(path);
        return path;
    }
}
