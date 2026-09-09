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
    private readonly List<string> _tempFiles = [];
    private readonly JsonlCiirReader _reader = new();
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly ICiirDocumentWriter _documentWriter = Substitute.For<ICiirDocumentWriter>();
    private readonly ICiirRelationWriter _relationWriter = Substitute.For<ICiirRelationWriter>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IEmbeddingFingerprintGenerator _fingerprintGenerator = new EmbeddingFingerprintGenerator();
    private readonly IRelationResolver _relationResolver = Substitute.For<IRelationResolver>();
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();
    private readonly IndexingOptions _options = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _projectIdsByName = new(StringComparer.Ordinal);
    private long _nextProjectId;

    public RunIndexationTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(2);
        _embeddingGenerator.BatchSize.Returns(32);

        // Mirrors the real IProjectStore's upsert semantics (same name -> same id) - ImportDocuments
        // and ImportRelations run concurrently (spec §4, via RunIndexation's Task.WhenAll), each
        // resolving the project independently, so both the id allocation and the cache lookup must
        // be genuinely thread-safe - a plain Dictionary raced here and handed the two use cases
        // different ids for the same project name, unlike the real Postgres-backed implementation's
        // atomic upsert.
        _projectStore
            .EnsureProjectAsync(Arg.Any<string>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var name = callInfo.ArgAt<string>(0);
                var id = _projectIdsByName.GetOrAdd(name, _ => Interlocked.Increment(ref _nextProjectId));
                return new Project { Id = id, Name = name, EmbeddingModel = callInfo.ArgAt<EmbeddingModel>(1) };
            });

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

        await CreateSut().ExecuteAsync(runId, path);

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

        await CreateSut().ExecuteAsync(runId, path);

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

        await CreateSut().ExecuteAsync(runId, path);

        Received.InOrder(() =>
        {
            _relationWriter.DeleteStaleAsync(Arg.Any<long>(), runId, Arg.Any<CancellationToken>());
            _documentWriter.DeleteStaleAsync(Arg.Any<long>(), runId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ExecuteAsync_DocumentImportThrows_MarksTheRunFailedAndNeverRunsCleanup()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _projectStore
            .EnsureProjectAsync(Arg.Any<string>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("database unavailable"));

        await CreateSut().ExecuteAsync(runId, path);

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
        _projectStore
            .EnsureProjectAsync(Arg.Any<string>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));

        await Should.NotThrowAsync(() => CreateSut().ExecuteAsync(runId, path));
    }

    [Fact]
    public async Task ExecuteAsync_ResolveRelationsThrows_MarksTheRunFailedAndNeverRunsCleanup()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _relationResolver.ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("resolver failed"));

        await CreateSut().ExecuteAsync(runId, path);

        await _runStore.Received(1).MarkStatusAsync(runId, IndexingStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _relationWriter.DidNotReceive().DeleteStaleAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringImport_MarksTheRunCancelledRatherThanFailed()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(BuildDocumentLine(Sha256Of("A"), relationCount: 1));
        _projectStore
            .EnsureProjectAsync(Arg.Any<string>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        await CreateSut().ExecuteAsync(runId, path);

        await _runStore.Received(1).MarkStatusAsync(runId, IndexingStatus.Cancelled, null, Arg.Any<CancellationToken>());
        await _runStore.DidNotReceive().MarkStatusAsync(runId, IndexingStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MultipleProjectsTouchedByTheRun_DeletesStaleEntitiesForEachOne()
    {
        var runId = Guid.NewGuid();
        var path = WriteJsonl(
            BuildDocumentLine(Sha256Of("A"), relationCount: 1, projectName: "ProjectA"),
            BuildDocumentLine(Sha256Of("B"), relationCount: 1, projectName: "ProjectB"));

        await CreateSut().ExecuteAsync(runId, path);

        await _relationResolver.Received(1).ResolveAsync(1, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _relationResolver.Received(1).ResolveAsync(2, Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _documentWriter.Received(1).DeleteStaleAsync(1, runId, Arg.Any<CancellationToken>());
        await _documentWriter.Received(1).DeleteStaleAsync(2, runId, Arg.Any<CancellationToken>());
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
            _reader, _projectStore, _documentWriter, _embeddingGenerator, _fingerprintGenerator, _options, NullLogger<ImportDocuments>.Instance);
        var importRelations = new ImportRelations(
            _reader, _projectStore, _relationWriter, _embeddingGenerator, _options, NullLogger<ImportRelations>.Instance);
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
