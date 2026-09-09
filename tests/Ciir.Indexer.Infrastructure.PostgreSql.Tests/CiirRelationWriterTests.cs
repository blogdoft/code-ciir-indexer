using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class CiirRelationWriterTests
{
    private readonly ICiirRelationWriter _sut;
    private readonly IProjectStore _projectStore;
    private readonly NpgsqlDataSource _dataSource;

    public CiirRelationWriterTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<ICiirRelationWriter>();
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
        _dataSource = fixture.GetDataSource();
    }

    [Fact]
    public async Task UpsertBatchAsync_RelationWhoseDocumentsDoNotExistYet_Succeeds()
    {
        var projectId = await CreateProjectAsync();
        var relation = TestData.BuildRelation(TestData.NewCiirId(), TestData.NewCiirId());

        await _sut.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        (await CountRelationsAsync(projectId)).ShouldBe(1);
    }

    [Fact]
    public async Task UpsertBatchAsync_SameRelationTwice_IsIdempotent()
    {
        var projectId = await CreateProjectAsync();
        var relation = TestData.BuildRelation(TestData.NewCiirId(), startLine: 42);

        await _sut.UpsertBatchAsync([relation], projectId, Guid.NewGuid());
        await _sut.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        (await CountRelationsAsync(projectId)).ShouldBe(1);
    }

    [Fact]
    public async Task UpsertBatchAsync_TwoOccurrencesAtDifferentLocations_CreatesTwoRows()
    {
        var projectId = await CreateProjectAsync();
        var sourceId = TestData.NewCiirId();
        var first = TestData.BuildRelation(sourceId, startLine: 1);
        var second = TestData.BuildRelation(sourceId, startLine: 2);

        await _sut.UpsertBatchAsync([first, second], projectId, Guid.NewGuid());

        (await CountRelationsAsync(projectId)).ShouldBe(2);
    }

    [Fact]
    public async Task UpsertBatchAsync_ExternalTargetWithoutCiirId_PersistsResolutionAsReported()
    {
        var projectId = await CreateProjectAsync();
        var relation = TestData.BuildRelation(
            TestData.NewCiirId(),
            targetCiirId: null,
            status: RelationResolutionStatus.External,
            origin: RelationResolutionOrigin.Framework);

        await _sut.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        var status = await GetResolutionStatusAsync(projectId, relation);
        status.ShouldBe("external");
    }

    [Fact]
    public async Task DeleteStaleAsync_RelationFromOlderRun_IsRemoved()
    {
        var projectId = await CreateProjectAsync();
        var staleRunId = Guid.NewGuid();
        var currentRunId = Guid.NewGuid();
        var staleRelation = TestData.BuildRelation(TestData.NewCiirId());
        var freshRelation = TestData.BuildRelation(TestData.NewCiirId());
        await _sut.UpsertBatchAsync([staleRelation], projectId, staleRunId);
        await _sut.UpsertBatchAsync([freshRelation], projectId, currentRunId);

        var deleted = await _sut.DeleteStaleAsync(projectId, currentRunId);

        deleted.ShouldBe(1);
        (await CountRelationsAsync(projectId)).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteStaleAsync_NoStaleRelations_DeletesNothing()
    {
        var projectId = await CreateProjectAsync();
        var runId = Guid.NewGuid();
        await _sut.UpsertBatchAsync([TestData.BuildRelation(TestData.NewCiirId())], projectId, runId);

        var deleted = await _sut.DeleteStaleAsync(projectId, runId);

        deleted.ShouldBe(0);
    }

    private async Task<long> CreateProjectAsync()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), new EmbeddingModel("bge-m3", PostgreSqlFixture.EmbeddingDimensions));
        return project.Id;
    }

    private async Task<int> CountRelationsAsync(long projectId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ciir_relations WHERE project_id = @ProjectId", new { ProjectId = projectId });
    }

    private async Task<string> GetResolutionStatusAsync(long projectId, CiirRelation relation)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<string>(
            "SELECT resolution_status FROM ciir_relations WHERE project_id = @ProjectId AND source_ciir_id = @SourceCiirId",
            new { ProjectId = projectId, SourceCiirId = relation.SourceCiirId.Value });
    }
}
