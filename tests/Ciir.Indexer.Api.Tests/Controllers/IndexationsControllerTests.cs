using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Controllers;

public sealed class IndexationsControllerTests
{
    private readonly IInputResolver _inputResolver = Substitute.For<IInputResolver>();
    private readonly IProjectStore _projectStore = Substitute.For<IProjectStore>();
    private readonly IEmbeddingGenerator _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();
    private readonly IIndexationQueue _queue = Substitute.For<IIndexationQueue>();

    public IndexationsControllerTests()
    {
        _embeddingGenerator.Model.Returns("bge-m3");
        _embeddingGenerator.Dimensions.Returns(1024);
    }

    [Fact]
    public async Task StartAsync_ValidRequest_ReturnsAcceptedWithTheRunIdAndStatus()
    {
        _inputResolver.ResolveAndValidate("/data/ciir/ciir.jsonl").Returns(Result<string>.FromSuccess("/data/ciir/ciir.jsonl"));
        var project = BuildProject();
        _projectStore
            .EnsureProjectAsync("MyProject", null, null, Arg.Any<EmbeddingModel>(), Arg.Any<CancellationToken>())
            .Returns(project);
        var run = BuildRun(IndexingStatus.Pending, project.Id);
        _runStore.CreateAsync("/data/ciir/ciir.jsonl", project.Id, Arg.Any<CancellationToken>()).Returns(run);

        var result = await CreateSut().StartAsync(
            new StartIndexationRequest("MyProject", "/data/ciir/ciir.jsonl"), CancellationToken.None);

        var accepted = result.ShouldBeOfType<AcceptedResult>();
        var body = accepted.Value.ShouldBeOfType<IndexationAcceptedResponse>();
        body.IndexationId.ShouldBe(run.Id);
        body.Status.ShouldBe("pending");
    }

    [Fact]
    public async Task StartAsync_InvalidPath_ReturnsTheMappedFailureResultWithoutCreatingARun()
    {
        _inputResolver
            .ResolveAndValidate(Arg.Any<string>())
            .Returns(Result<string>.FromFailure(new Failure("403-path-not-allowed", "not allowed")));

        var result = await CreateSut().StartAsync(
            new StartIndexationRequest("MyProject", "/etc/passwd"), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        await _runStore.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_BlankProjectName_ReturnsBadRequestWithoutCreatingARun()
    {
        var result = await CreateSut().StartAsync(
            new StartIndexationRequest(" ", "/data/ciir/ciir.jsonl"), CancellationToken.None);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        await _runStore.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ExistingRun_ReturnsOkWithTheMappedStatusResponse()
    {
        var run = BuildRun(
            IndexingStatus.Completed,
            projectId: 1,
            new IndexingCounters
            {
                DocumentsProcessed = 10,
                DocumentsInserted = 7,
                DocumentsUpdated = 3,
                EmbeddingsGenerated = 2,
                EmbeddingsReused = 8,
                RelationsProcessed = 20,
                RelationsResolved = 18,
                RelationsUnresolved = 2,
            });
        _runStore.GetAsync(run.Id, Arg.Any<CancellationToken>()).Returns(run);

        var result = await CreateSut().GetAsync(run.Id, CancellationToken.None);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var body = ok.Value.ShouldBeOfType<IndexationStatusResponse>();
        body.Id.ShouldBe(run.Id);
        body.Status.ShouldBe("completed");
        body.Documents.ShouldBe(new IndexationDocumentsSummary(10, 7, 3, 2, 8));
        body.Relations.ShouldBe(new IndexationRelationsSummary(20, 18, 2));
    }

    [Fact]
    public async Task GetAsync_UnknownRun_ReturnsNotFound()
    {
        _runStore.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IndexingRun?)null);

        var result = await CreateSut().GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.ShouldBeOfType<NotFoundResult>();
    }

    private static Project BuildProject() => new()
    {
        Id = 1,
        Name = "MyProject",
        EmbeddingModel = new EmbeddingModel("bge-m3", 1024),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static IndexingRun BuildRun(IndexingStatus status, long projectId, IndexingCounters? counters = null) => new()
    {
        Id = Guid.NewGuid(),
        Path = "/data/ciir/ciir.jsonl",
        ProjectId = projectId,
        Status = status,
        StartedAt = DateTimeOffset.UtcNow,
        Counters = counters ?? new IndexingCounters(),
    };

    private IndexationsController CreateSut()
    {
        var startIndexation = new StartIndexation(_inputResolver, _projectStore, _embeddingGenerator, _runStore, _queue);
        return new IndexationsController(startIndexation, _runStore)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
