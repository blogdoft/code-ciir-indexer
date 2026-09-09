using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class IndexingRunStoreTests
{
    private readonly IIndexingRunStore _sut;

    public IndexingRunStoreTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<IIndexingRunStore>();
    }

    [Fact]
    public async Task CreateAsync_NewRun_StartsInPendingStatus()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");

        run.Id.ShouldNotBe(Guid.Empty);
        run.Path.ShouldBe("/data/ciir/ciir.jsonl");
        run.Status.ShouldBe(IndexingStatus.Pending);
        run.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCountersAsync_ThenGetAsync_ReflectsTheUpdatedCounters()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");
        var counters = new IndexingCounters
        {
            DocumentsProcessed = 10,
            DocumentsInserted = 7,
            DocumentsUpdated = 3,
            EmbeddingsGenerated = 5,
            EmbeddingsReused = 5,
            RelationsProcessed = 20,
            RelationsResolved = 18,
            RelationsUnresolved = 2,
        };

        await _sut.UpdateCountersAsync(run.Id, counters);
        var reloaded = await _sut.GetAsync(run.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Counters.ShouldBe(counters);
    }

    [Fact]
    public async Task MarkStatusAsync_Completed_SetsStatusAndFinishedAt()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");

        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Completed);
        var reloaded = await _sut.GetAsync(run.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Status.ShouldBe(IndexingStatus.Completed);
        reloaded.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task MarkStatusAsync_Failed_StoresTheErrorMessage()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");

        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Failed, "boom");
        var reloaded = await _sut.GetAsync(run.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Status.ShouldBe(IndexingStatus.Failed);
        reloaded.Error.ShouldBe("boom");
    }

    [Fact]
    public async Task GetAsync_UnknownRunId_ReturnsNull()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_RunLeftRunning_IsMarkedFailed()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");
        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Running);

        var reconciled = await _sut.ReconcileOrphanedRunsAsync();

        reconciled.ShouldContain(run.Id);
        var reloaded = await _sut.GetAsync(run.Id);
        reloaded!.Status.ShouldBe(IndexingStatus.Failed);
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_RunAlreadyCompleted_IsNotTouched()
    {
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl");
        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Completed);

        var reconciled = await _sut.ReconcileOrphanedRunsAsync();

        reconciled.ShouldNotContain(run.Id);
    }
}
