using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Controllers;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;

namespace Ciir.Indexer.Api.Tests.Controllers;

public sealed class IndexationsControllerTests
{
    private readonly IIndexingRunStore _runStore = Substitute.For<IIndexingRunStore>();

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

    private static IndexingRun BuildRun(IndexingStatus status, long projectId, IndexingCounters? counters = null) =>
        IndexingRun.Create(
            Guid.NewGuid(),
            "/data/ciir/ciir.jsonl",
            projectId,
            status,
            DateTimeOffset.UtcNow,
            counters: counters).Value;

    private IndexationsController CreateSut() => new(_runStore);
}
