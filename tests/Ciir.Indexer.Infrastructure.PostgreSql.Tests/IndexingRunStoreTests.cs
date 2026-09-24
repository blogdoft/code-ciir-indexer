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
    private readonly IProjectStore _projectStore;

    public IndexingRunStoreTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<IIndexingRunStore>();
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
    }

    [Fact]
    public async Task CreateAsync_NewRun_StartsInPendingStatus()
    {
        var projectId = await CreateProjectAsync();

        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);

        run.Id.ShouldNotBe(Guid.Empty);
        run.Path.ShouldBe("/data/ciir/ciir.jsonl");
        run.ProjectId.ShouldBe(projectId);
        run.Status.ShouldBe(IndexingStatus.Pending);
        run.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCountersAsync_ThenGetAsync_ReflectsTheUpdatedCounters()
    {
        var projectId = await CreateProjectAsync();
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);
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
        var projectId = await CreateProjectAsync();
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);

        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Completed);
        var reloaded = await _sut.GetAsync(run.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Status.ShouldBe(IndexingStatus.Completed);
        reloaded.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task MarkStatusAsync_Failed_StoresTheErrorMessage()
    {
        var projectId = await CreateProjectAsync();
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);

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
        var projectId = await CreateProjectAsync();
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);
        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Running);

        var reconciled = await _sut.ReconcileOrphanedRunsAsync();

        reconciled.ShouldContain(run.Id);
        var reloaded = await _sut.GetAsync(run.Id);
        reloaded!.Status.ShouldBe(IndexingStatus.Failed);
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_RunAlreadyCompleted_IsNotTouched()
    {
        var projectId = await CreateProjectAsync();
        var run = await _sut.CreateAsync("/data/ciir/ciir.jsonl", projectId);
        await _sut.MarkStatusAsync(run.Id, IndexingStatus.Completed);

        var reconciled = await _sut.ReconcileOrphanedRunsAsync();

        reconciled.ShouldNotContain(run.Id);
    }

    private async Task<long> CreateProjectAsync()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, EmbeddingModel.Create("bge-m3", PostgreSqlFixture.EmbeddingDimensions).Value);
        return project.Id;
    }
}
