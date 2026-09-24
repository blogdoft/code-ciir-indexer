using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class ProcessNextCiirUploadTests
{
    private readonly ICiirUploadStore _uploadStore = Substitute.For<ICiirUploadStore>();
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly ICiirJsonlReader _reader = Substitute.For<ICiirJsonlReader>();
    private readonly ICiirDocumentWriter _documentWriter = Substitute.For<ICiirDocumentWriter>();
    private readonly ICiirRelationWriter _relationWriter = Substitute.For<ICiirRelationWriter>();
    private readonly IRelationResolver _relationResolver = Substitute.For<IRelationResolver>();
    private readonly UploadOptions _options = new()
    {
        StagingDirectory = Path.Combine(Path.GetTempPath(), "ciir-upload-tests-" + Guid.NewGuid().ToString("N")),
    };

    public ProcessNextCiirUploadTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(2);
        _embeddingGenerator.BatchSize.Returns(32);
        _reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(EmptyRecords());
        _documentWriter
            .GetExistingFingerprintsAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?>());
        _relationResolver
            .ResolveAsync(Arg.Any<long>(), Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new RelationResolutionCounters());
    }

    [Fact]
    public async Task ExecuteAsync_NothingEligible_ReturnsFalse()
    {
        StubNothingEligible();

        var processed = await CreateSut().ExecuteAsync();

        processed.ShouldBeFalse();
        await _projectStore.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustedUploads_DeletesTheirObjectsEvenWhenNothingIsClaimed()
    {
        var exhausted = BuildUpload(status: CiirUploadStatus.Processing, retryCount: 3);
        _uploadStore.ReclaimExhaustedAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([exhausted]);
        _uploadStore.ClaimNextAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((CiirUpload?)null);

        await CreateSut().ExecuteAsync();

        await _objectStorage.Received(1).DeleteAsync(exhausted.Bucket, exhausted.ObjectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ClaimedUploadWhoseProjectNoLongerExists_MarksFailedWithoutDownloading()
    {
        var upload = BuildUpload();
        StubNothingEligible();
        _uploadStore.ClaimNextAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(upload);
        _projectStore.GetByIdAsync(upload.ProjectId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var processed = await CreateSut().ExecuteAsync();

        processed.ShouldBeTrue();
        await _objectStorage.DidNotReceive().DownloadToFileAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _objectStorage.Received(1).DeleteAsync(upload.Bucket, upload.ObjectKey, Arg.Any<CancellationToken>());
        await _uploadStore.Received(1).MarkFailedAsync(upload.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulIndexation_MarksProcessedAndDeletesTheObject()
    {
        var upload = BuildUpload();
        var project = BuildProject(upload.ProjectId);
        var run = BuildRun(upload.ProjectId, IndexingStatus.Completed);
        SetUpClaimedUpload(upload, project, run);

        var processed = await CreateSut().ExecuteAsync();

        processed.ShouldBeTrue();
        await _uploadStore.Received(1).MarkIndexingRunAsync(upload.Id, run.Id, Arg.Any<CancellationToken>());
        await _objectStorage.Received(1).DeleteAsync(upload.Bucket, upload.ObjectKey, Arg.Any<CancellationToken>());
        await _uploadStore.Received(1).MarkProcessedAsync(upload.Id, Arg.Any<CancellationToken>());
        await _uploadStore.DidNotReceive().MarkFailedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IndexationDoesNotComplete_MarksFailedWithTheRunsError()
    {
        var upload = BuildUpload();
        var project = BuildProject(upload.ProjectId);
        var run = BuildRun(upload.ProjectId, IndexingStatus.Failed, error: "boom");
        SetUpClaimedUpload(upload, project, run);

        var processed = await CreateSut().ExecuteAsync();

        processed.ShouldBeTrue();
        await _uploadStore.Received(1).MarkFailedAsync(upload.Id, "boom", Arg.Any<CancellationToken>());
        await _uploadStore.DidNotReceive().MarkProcessedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _objectStorage.Received(1).DeleteAsync(upload.Bucket, upload.ObjectKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AfterProcessing_DeletesTheLocalStagingDirectory()
    {
        var upload = BuildUpload();
        var project = BuildProject(upload.ProjectId);
        var run = BuildRun(upload.ProjectId, IndexingStatus.Completed);
        SetUpClaimedUpload(upload, project, run);
        var expectedStagingDirectory = Path.Combine(_options.StagingDirectory, upload.Id.ToString("N"));

        await CreateSut().ExecuteAsync();

        Directory.Exists(expectedStagingDirectory).ShouldBeFalse();
    }

    private static CiirUpload BuildUpload(CiirUploadStatus status = CiirUploadStatus.Pending, int retryCount = 0) =>
        CiirUpload.Create(
            Guid.NewGuid(),
            42,
            "ciir-uploads",
            $"{Guid.NewGuid():N}/ciir.jsonl",
            status,
            DateTimeOffset.UtcNow,
            retryCount: retryCount).Value;

    private static Project BuildProject(long id) => Project.Create(
        "MyProject",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 2).Value,
        id).Value;

    private static IndexingRun BuildRun(long projectId, IndexingStatus status, string? error = null) => IndexingRun.Create(
        Guid.NewGuid(),
        "/tmp/ciir.jsonl",
        projectId,
        status,
        DateTimeOffset.UtcNow,
        error: error).Value;

    private static async IAsyncEnumerable<CiirRecordReadResult> EmptyRecords()
    {
        await Task.CompletedTask;
        yield break;
    }

    private void StubNothingEligible()
    {
        _uploadStore.ReclaimExhaustedAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _uploadStore.ClaimNextAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((CiirUpload?)null);
    }

    private void SetUpClaimedUpload(CiirUpload upload, Project project, IndexingRun run)
    {
        StubNothingEligible();
        _uploadStore.ClaimNextAsync(Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(upload);
        _projectStore.GetByIdAsync(upload.ProjectId, Arg.Any<CancellationToken>()).Returns(project);
        _projectStore
            .EnsureProjectAsync(project.Name, project.GitUrl, project.GitRawUrl, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(project);
        _runStore.CreateAsync(Arg.Any<string>(), project.Id, Arg.Any<CancellationToken>()).Returns(run);
        _runStore.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);
    }

    private ProcessNextCiirUpload CreateSut()
    {
        var indexingOptions = new IndexingOptions();
        var fingerprintGenerator = new EmbeddingFingerprintGenerator();
        var importDocuments = new ImportDocuments(
            _reader, _documentWriter, _embeddingGenerator, fingerprintGenerator, indexingOptions, NullLogger<ImportDocuments>.Instance);
        var importRelations = new ImportRelations(_reader, _relationWriter, indexingOptions, NullLogger<ImportRelations>.Instance);
        var resolveRelations = new ResolveRelations(_relationResolver);
        var runIndexation = new RunIndexation(
            importDocuments, importRelations, resolveRelations, _documentWriter, _relationWriter, _runStore, NullLogger<RunIndexation>.Instance);

        return new ProcessNextCiirUpload(
            _uploadStore,
            _objectStorage,
            _projectStore,
            _runStore,
            _embeddingGenerator,
            runIndexation,
            _options,
            NullLogger<ProcessNextCiirUpload>.Instance);
    }
}
