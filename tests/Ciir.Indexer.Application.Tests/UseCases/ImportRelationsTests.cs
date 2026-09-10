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
    private const long ProjectId = 1;

    private readonly List<string> _tempFiles = [];
    private readonly JsonlCiirReader _reader = new();
    private readonly ICiirRelationWriter _relationWriter = Substitute.For<ICiirRelationWriter>();
    private readonly IndexingOptions _options = new();

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

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

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

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

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

        await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        // First flush at the batch-size threshold (2 relations from A), second flush for the
        // remaining 1 relation from B at end-of-file.
        await _relationWriter.Received(2).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWithDifferentProjectFields_AllLandUnderTheSinglePassedInProject()
    {
        // The record's own "project" field (spec's per-record CIIR contract field) must be ignored
        // for identity - every relation in the file binds to the caller-supplied projectId
        // regardless (spec's "Atualização — Identidade de projeto informada pelo chamador").
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), relationCount: 1, projectName: "ProjectA"),
            BuildDocumentLine(Sha256Of("B"), relationCount: 1, projectName: "ProjectB"));

        await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

        await _relationWriter.Received(1).UpsertBatchAsync(
            Arg.Is<IReadOnlyCollection<CiirRelation>>(batch => batch.Count == 2),
            ProjectId,
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RunId_IsStampedOnEveryUpsertBatch()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));

        await CreateSut().ExecuteAsync(path, runId, ProjectId);

        await _relationWriter.Received(1).UpsertBatchAsync(
            Arg.Any<IReadOnlyCollection<CiirRelation>>(), Arg.Any<long>(), runId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MalformedLine_IsSkippedAndSubsequentValidRecordsAreStillImported()
    {
        var validLine = BuildDocumentLine(Sha256Of("A"), relationCount: 1);
        var path = WriteJsonl("{ not valid json", validLine);

        var result = await CreateSut().ExecuteAsync(path, Guid.NewGuid(), ProjectId);

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
        new(_reader, _relationWriter, _options, NullLogger<ImportRelations>.Instance);

    private string WriteJsonl(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        _tempFiles.Add(path);
        return path;
    }
}
