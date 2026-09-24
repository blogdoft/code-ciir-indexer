using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class RegisterCiirUploadTests
{
    private const string Bucket = "ciir-uploads";
    private const long InternalProjectId = 42;

    private static readonly Guid ProjectPublicId = Guid.NewGuid();

    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IObjectStorage _objectStorage = Substitute.For<IObjectStorage>();
    private readonly ICiirUploadStore _uploadStore = Substitute.For<ICiirUploadStore>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public async Task ExecuteAsync_InvalidProjectId_FailsBeforeCheckingAnything(string? projectId)
    {
        var result = await CreateSut().ExecuteAsync(projectId, "abc/ciir.jsonl");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-project-id-required");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _objectStorage.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_MissingObjectKey_ReturnsObjectKeyRequired(string? objectKey)
    {
        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), objectKey);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-object-key-required");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WrongExtension_FailsBeforeCheckingTheProject()
    {
        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "abc/ciir.json");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-invalid-extension");
        await _projectStore.DidNotReceive().GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProject_ReturnsNotFoundWithoutCheckingTheObject()
    {
        _projectStore.GetByPublicIdAsync(ProjectPublicId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "abc/ciir.jsonl");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-project-not-found");
        await _objectStorage.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ObjectDoesNotExist_ReturnsObjectNotFoundWithoutRegisteringAnything()
    {
        _projectStore.GetByPublicIdAsync(ProjectPublicId, Arg.Any<CancellationToken>()).Returns(BuildProject());
        _objectStorage.ExistsAsync(Bucket, "abc/ciir.jsonl", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "abc/ciir.jsonl");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("404-object-not-found");
        await _uploadStore.DidNotReceive().CreateAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_RegistersThePendingRecordWithoutTouchingTheObjectBytes()
    {
        _projectStore.GetByPublicIdAsync(ProjectPublicId, Arg.Any<CancellationToken>()).Returns(BuildProject());
        _objectStorage.ExistsAsync(Bucket, "abc/ciir.jsonl", Arg.Any<CancellationToken>()).Returns(true);
        var upload = CiirUpload.Create(
            Guid.NewGuid(), InternalProjectId, Bucket, "abc/ciir.jsonl", CiirUploadStatus.Pending, DateTimeOffset.UtcNow).Value;
        _uploadStore.CreateAsync(InternalProjectId, Bucket, "abc/ciir.jsonl", Arg.Any<CancellationToken>()).Returns(upload);

        var result = await CreateSut().ExecuteAsync(ProjectPublicId.ToString(), "abc/ciir.jsonl");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(upload);
        await _objectStorage.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static Project BuildProject() => Project.Create(
        "MyProject",
        gitUrl: null,
        gitRawUrl: null,
        EmbeddingModel.Create("bge-m3", 1024).Value,
        publicId: ProjectPublicId,
        id: InternalProjectId).Value;

    private RegisterCiirUpload CreateSut() => new(_projectStore, _objectStorage, _uploadStore, Bucket);
}
