using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class SubmitCiirUploadTests
{
    private const string Bucket = "ciir-uploads";
    private const long InternalProjectId = 42;

    private static readonly Guid ProjectPublicId = Guid.NewGuid();

    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly ICiirUploadStore _uploadStore = Substitute.For<ICiirUploadStore>();
    private readonly UploadOptions _options = new() { MaxCiirFileSizeBytes = 1024 };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task ExecuteAsync_InvalidProjectId_FailsBeforeTouchingObjectStorage(string? projectId)
    {
        var result = await CreateSut().ExecuteAsync(projectId, "ciir.jsonl", Stream.Null, contentLength: 10);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-project-id-required");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _objectStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WrongExtension_FailsBeforeCheckingTheProject()
    {
        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "ciir.json", Stream.Null, contentLength: 10);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-invalid-extension");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ContentLengthAboveTheLimit_FailsBeforeCheckingTheProject()
    {
        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "ciir.jsonl", Stream.Null, contentLength: 2048);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("413-file-too-large");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProject_ReturnsNotFoundWithoutUploading()
    {
        _projectStore.GetByPublicIdAsync(ProjectPublicId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "ciir.jsonl", Stream.Null, contentLength: 10);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-project-not-found");
        await _objectStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_UploadsThenCreatesThePendingRecord()
    {
        var project = Project.Create(
            "MyProject",
            gitUrl: null,
            gitRawUrl: null,
            EmbeddingModel.Create("bge-m3", 1024).Value,
            publicId: ProjectPublicId,
            id: InternalProjectId).Value;
        _projectStore.GetByPublicIdAsync(ProjectPublicId, Arg.Any<CancellationToken>()).Returns(project);
        using var content = new MemoryStream([1, 2, 3]);
        var upload = CiirUpload.Create(
            Guid.NewGuid(), InternalProjectId, Bucket, "abc/ciir.jsonl", CiirUploadStatus.Pending, DateTimeOffset.UtcNow).Value;
        _uploadStore.CreateAsync(InternalProjectId, Bucket, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(upload);

        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "ciir.jsonl", content, contentLength: 3);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(upload);
        await _objectStorage.Received(1).UploadAsync(
            Bucket, Arg.Is<string>(key => key.EndsWith("/ciir.jsonl", StringComparison.Ordinal)), content, 3, "application/x-ndjson", Arg.Any<CancellationToken>());
    }

    private SubmitCiirUpload CreateSut() => new(_projectStore, _objectStorage, _uploadStore, Bucket, _options);
}
