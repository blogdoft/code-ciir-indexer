using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Application.Tests.UseCases;

public sealed class StartIndexationTests
{
    private readonly IInputResolver _inputResolver = Substitute.For<IInputResolver>();
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();
    private readonly IIndexationQueue _queue = Substitute.For<IIndexationQueue>();

    public StartIndexationTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(1024);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BlankProjectName_FailsBeforeValidatingThePath(string blankProjectName)
    {
        var result = await CreateSut().ExecuteAsync(blankProjectName, "/data/ciir/ciir.jsonl");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("400-project-name-required");
        _inputResolver.DidNotReceive().ResolveAndValidate(Arg.Any<string>());
        await _projectStore.DidNotReceive().EnsureProjectAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPath_ReturnsThePathsOwnFailure()
    {
        _inputResolver
            .ResolveAndValidate("/etc/passwd")
            .Returns(Result<string>.FromFailure(new Failure("403-path-not-allowed", "not allowed")));

        var result = await CreateSut().ExecuteAsync("MyProject", "/etc/passwd");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe("403-path-not-allowed");
        await _projectStore.DidNotReceive().EnsureProjectAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidRequest_ResolvesTheProjectThenCreatesAndEnqueuesTheRun()
    {
        _inputResolver.ResolveAndValidate("/data/ciir/ciir.jsonl").Returns(Result<string>.FromSuccess("/data/ciir/ciir.jsonl"));
        var project = new Project
        {
            Id = 42,
            Name = "MyProject",
            EmbeddingModel = new EmbeddingModel("bge-m3", 1024),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _projectStore
            .EnsureProjectAsync("MyProject", "https://git.example/repo", "https://raw.example/repo", Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(project);
        var run = new IndexingRun
        {
            Id = Guid.NewGuid(),
            Path = "/data/ciir/ciir.jsonl",
            ProjectId = project.Id,
            Status = IndexingStatus.Pending,
            StartedAt = DateTimeOffset.UtcNow,
        };
        _runStore.CreateAsync("/data/ciir/ciir.jsonl", project.Id, Arg.Any<CancellationToken>()).Returns(run);

        var result = await CreateSut().ExecuteAsync(
            "MyProject", "/data/ciir/ciir.jsonl", "https://git.example/repo", "https://raw.example/repo");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(run);
        await _queue.Received(1).EnqueueAsync(run.Id, Arg.Any<CancellationToken>());
    }

    private StartIndexation CreateSut() =>
        new(_inputResolver, _projectStore, _embeddingGenerator, _runStore, _queue);
}
