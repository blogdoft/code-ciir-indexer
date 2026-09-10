using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class RunIndexationTests : IDisposable
{
    private const long ProjectId = 1;

    private readonly List<string> _tempFiles = [];
    private readonly JsonlCiirReader _reader = new();
    private readonly ICiirDocumentWriter _documentWriter = Substitute.For<ICiirDocumentWriter>();
    private readonly ICiirRelationWriter _relationWriter = Substitute.For<ICiirRelationWriter>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IEmbeddingFingerprintGenerator _fingerprintGenerator = new EmbeddingFingerprintGenerator();
    private readonly IRelationResolver _relationResolver = Substitute.For<IRelationResolver>();
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();
    private readonly IndexingOptions _options = new();

    public RunIndexationTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(2);
        _embeddingGenerator.BatchSize.Returns(32);

        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?>());

        _relationResolver
            .ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new RelationResolutionCounters { RelationsTotal = 1, TargetResolved = 1 });
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_TransitionsThroughRunningResolvingRelationsThenCompleted()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        Received.InOrder(() =>
        {
            _runStore.MarkStatusAsync(runId, IndexingStatus.Running, null, Arg.Any<CancellationToken>());
            _runStore.MarkStatusAsync(runId, IndexingStatus.ResolvingRelations, null, Arg.Any<CancellationToken>());
            _runStore.MarkStatusAsync(runId, IndexingStatus.Completed, null, Arg.Any<CancellationToken>());
        });
        await _runStore.DidNotReceive().MarkStatusAsync(runId, IndexingStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _runStore.DidNotReceive().MarkStatusAsync(runId, IndexingStatus.Cancelled, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_UpdatesCountersWithTheMergedResultBeforeCompleting()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        await _runStore.Received(1).UpdateCountersAsync(
            runId,
            Arg.Is<IndexingCounters>(counters =>
                counters.DocumentsProcessed == 1
                && counters.RelationsProcessed == 1
                && counters.RelationsResolved == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulRun_DeletesStaleRelationsBeforeStaleDocuments()
    {
        // Spec §29's finalization order: relations before documents (FK dependency, §58).
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        Received.InOrder(() =>
        {
            _relationWriter.DeleteStaleAsync(ProjectId, runId, Arg.Any<CancellationToken>());
            _documentWriter.DeleteStaleAsync(ProjectId, runId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExecuteAsync_DocumentImportThrows_MarksTheRunFailedAndNeverRunsCleanup()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("database unavailable"));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        await _runStore.Received(1).MarkStatusAsync(
            runId, IndexingStatus.Failed, Arg.Is<string>(m => m.Contains("database unavailable")), Arg.Any<CancellationToken>());
        await _documentWriter.DidNotReceive().DeleteStaleAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _relationWriter.DidNotReceive().DeleteStaleAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _runStore.DidNotReceive().UpdateCountersAsync(Arg.Any<Guid>(), Arg.Any<IndexingCounters>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailingDependency_NeverThrowsOutOfExecuteAsync()
    {
        // RunIndexation's contract: always leave the run in a terminal, queryable status - never
        // leak an exception to the caller, which is a fire-and-forget background worker (spec §33).
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        await Should.NotThrowAsync(() => CreateSut().ExecuteAsync(runId, path, ProjectId));
    }

    [Fact]
    public async Task ExecuteAsync_ResolveRelationsThrows_MarksTheRunFailedAndNeverRunsCleanup()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _relationResolver.ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("resolver failed"));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        await _runStore.Received(1).MarkStatusAsync(runId, IndexingStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _relationWriter.DidNotReceive().DeleteStaleAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringImport_MarksTheRunCancelledRatherThanFailed()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        await _runStore.Received(1).MarkStatusAsync(runId, IndexingStatus.Cancelled, null, Arg.Any<CancellationToken>());
        await _runStore.DidNotReceive().MarkStatusAsync(runId, IndexingStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsWithDifferentProjectFields_AllBindToTheSinglePassedInProject()
    {
        // The record's own "project" field is ignored for identity - a single run always resolves
        // relations/cleanup for exactly the caller-supplied projectId (spec's "Atualização —
        // Identidade de projeto informada pelo chamador"), regardless of what each record claims.
        var runId = Guid.NewGuid();
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), relationCount: 1, projectName: "ProjectA"),
            BuildDocumentLine(Sha256Of("B"), relationCount: 1, projectName: "ProjectB"));

        await CreateSut().ExecuteAsync(runId, path, ProjectId);

        await _relationResolver.Received(1).ResolveAsync(
            ProjectId, Arg.Is<IReadOnlyCollection<long>>(ids => ids.Count == 1 && ids.Contains(ProjectId)), Arg.Any<CancellationToken>());
        await _documentWriter.Received(1).DeleteStaleAsync(ProjectId, runId, Arg.Any<CancellationToken>());
        await _relationWriter.Received(1).DeleteStaleAsync(ProjectId, runId, Arg.Any<CancellationToken>());
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

    private RunIndexation CreateSut()
    {
        var importDocuments = new ImportDocuments(
            _reader, _documentWriter, _embeddingGenerator, _fingerprintGenerator, _options, NullLogger<ImportDocuments>.Instance);
        var importRelations = new ImportRelations(
            _reader, _relationWriter, _options, NullLogger<ImportRelations>.Instance);
        var resolveRelations = new ResolveRelations(_relationResolver);

        return new RunIndexation(
            importDocuments, importRelations, resolveRelations, _documentWriter, _relationWriter, _runStore, NullLogger<RunIndexation>.Instance);
    }

    private string WriteJsonl(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        _tempFiles.Add(path);
        return path;
    }
}
