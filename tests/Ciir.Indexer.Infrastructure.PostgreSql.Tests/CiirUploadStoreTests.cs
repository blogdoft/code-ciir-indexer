using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

// Unlike ciir_documents/ciir_relations/indexing_runs, ClaimNextAsync/ReclaimExhaustedAsync
// deliberately query across every project's uploads (upload spec §8 - the worker is a single
// global queue, not scoped per project), so tests here cannot isolate themselves by using a
// unique project name the way the other stores' tests do. IAsyncLifetime clears the table before
// each test instead, safe because PostgreSqlCollection already serializes every test class here
// against the one shared container.
[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class CiirUploadStoreTests : IAsyncLifetime
{
    private const string Bucket = "ciir-uploads";

    private readonly ICiirUploadStore _sut;
    private readonly IProjectStore _projectStore;
    private readonly IIndexingRunStore _runStore;
    private readonly NpgsqlDataSource _dataSource;

    public CiirUploadStoreTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<ICiirUploadStore>();
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
        _runStore = fixture.Services.GetRequiredService<IIndexingRunStore>();
        _dataSource = fixture.GetDataSource();
    }

    public async Task InitializeAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM ciir_uploads;", connection);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateAsync_NewUpload_StartsInPendingStatus()
    {
        var projectId = await CreateProjectAsync();

        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        upload.Id.ShouldNotBe(Guid.Empty);
        upload.ProjectId.ShouldBe(projectId);
        upload.Status.ShouldBe(CiirUploadStatus.Pending);
        upload.RetryCount.ShouldBe(0);
        upload.ProcessingStartedAt.ShouldBeNull();
        upload.ProcessedAt.ShouldBeNull();
        upload.IndexingRunId.ShouldBeNull();
    }

    [Fact]
    public async Task ClaimNextAsync_NothingPending_ReturnsNull()
    {
        var claimed = await _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3);

        claimed.ShouldBeNull();
    }

    [Fact]
    public async Task ClaimNextAsync_OnePending_ClaimsItAndTransitionsToProcessing()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        var claimed = await _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3);

        claimed.ShouldNotBeNull();
        claimed.Id.ShouldBe(upload.Id);
        claimed.Status.ShouldBe(CiirUploadStatus.Processing);
        claimed.ProcessingStartedAt.ShouldNotBeNull();
        claimed.RetryCount.ShouldBe(0);
    }

    [Fact]
    public async Task ClaimNextAsync_TwoPendingRows_OldestIsClaimedFirst()
    {
        var projectId = await CreateProjectAsync();
        var older = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        await Task.Delay(10);
        await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        var claimed = await _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3);

        claimed.ShouldNotBeNull();
        claimed.Id.ShouldBe(older.Id);
    }

    [Fact]
    public async Task ClaimNextAsync_FreshlyProcessingRow_IsNotReclaimed()
    {
        var projectId = await CreateProjectAsync();
        await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        await _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3);

        var claimed = await _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3);

        claimed.ShouldBeNull();
    }

    [Fact]
    public async Task ClaimNextAsync_StuckProcessingRowWithinRetryBudget_IsReclaimedWithIncrementedRetryCount()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        await _sut.ClaimNextAsync(TimeSpan.Zero, maxRetryCount: 3);

        var reclaimed = await _sut.ClaimNextAsync(TimeSpan.Zero, maxRetryCount: 3);

        reclaimed.ShouldNotBeNull();
        reclaimed.Id.ShouldBe(upload.Id);
        reclaimed.RetryCount.ShouldBe(1);
    }

    [Fact]
    public async Task ClaimNextAsync_TwoPendingUploadsClaimedConcurrently_EachIsClaimedExactlyOnce()
    {
        var projectId = await CreateProjectAsync();
        var first = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        var second = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        var results = await Task.WhenAll(
            _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3),
            _sut.ClaimNextAsync(TimeSpan.FromMinutes(30), maxRetryCount: 3));

        var claimedIds = results.Where(r => r is not null).Select(r => r!.Id).ToList();
        claimedIds.ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task ReclaimExhaustedAsync_StuckRowAtMaxRetryCount_MarksItFailedAndStopsClaimingIt()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        await _sut.ClaimNextAsync(TimeSpan.Zero, maxRetryCount: 1);
        await _sut.ClaimNextAsync(TimeSpan.Zero, maxRetryCount: 1);

        var exhausted = await _sut.ReclaimExhaustedAsync(TimeSpan.Zero, maxRetryCount: 1);

        exhausted.ShouldContain(u => u.Id == upload.Id);
        var reloaded = await _sut.GetAsync(upload.Id);
        reloaded!.Status.ShouldBe(CiirUploadStatus.Failed);
        (await _sut.ClaimNextAsync(TimeSpan.Zero, maxRetryCount: 1)).ShouldBeNull();
    }

    [Fact]
    public async Task MarkIndexingRunAsync_ThenGetAsync_ReflectsTheLinkedRun()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());
        var run = await _runStore.CreateAsync("/data/ciir/ciir.jsonl", projectId);

        await _sut.MarkIndexingRunAsync(upload.Id, run.Id);
        var reloaded = await _sut.GetAsync(upload.Id);

        reloaded!.IndexingRunId.ShouldBe(run.Id);
    }

    [Fact]
    public async Task MarkProcessedAsync_SetsStatusAndProcessedAt()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        await _sut.MarkProcessedAsync(upload.Id);
        var reloaded = await _sut.GetAsync(upload.Id);

        reloaded!.Status.ShouldBe(CiirUploadStatus.Processed);
        reloaded.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task MarkFailedAsync_SetsStatusErrorAndProcessedAt()
    {
        var projectId = await CreateProjectAsync();
        var upload = await _sut.CreateAsync(projectId, Bucket, NewObjectKey());

        await _sut.MarkFailedAsync(upload.Id, "boom");
        var reloaded = await _sut.GetAsync(upload.Id);

        reloaded!.Status.ShouldBe(CiirUploadStatus.Failed);
        reloaded.Error.ShouldBe("boom");
        reloaded.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetAsync_UnknownUploadId_ReturnsNull()
    {
        var result = await _sut.GetAsync(Guid.NewGuid());

        result.ShouldBeNull();
    }

    private static string NewObjectKey() => $"{Guid.NewGuid():N}/ciir.jsonl";

    private async Task<long> CreateProjectAsync()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, new EmbeddingModel("bge-m3", PostgreSqlFixture.EmbeddingDimensions));
        return project.Id;
    }
}
