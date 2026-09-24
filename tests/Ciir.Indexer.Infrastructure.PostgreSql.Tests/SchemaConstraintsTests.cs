using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class SchemaConstraintsTests
{
    private readonly IProjectStore _projectStore;
    private readonly ICiirDocumentWriter _documentWriter;
    private readonly ICiirRelationWriter _relationWriter;
    private readonly IRelationResolver _relationResolver;
    private readonly NpgsqlDataSource _dataSource;

    public SchemaConstraintsTests(PostgreSqlFixture fixture)
    {
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
        _documentWriter = fixture.Services.GetRequiredService<ICiirDocumentWriter>();
        _relationWriter = fixture.Services.GetRequiredService<ICiirRelationWriter>();
        _relationResolver = fixture.Services.GetRequiredService<IRelationResolver>();
        _dataSource = fixture.GetDataSource();
    }

    [Fact]
    public async Task Ciir_documents_RawDuplicateInsert_ViolatesTheProjectCiirIdUniqueConstraint()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, EmbeddingModel.Create("bge-m3", PostgreSqlFixture.EmbeddingDimensions).Value);
        var document = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(document)], project.Id, Guid.NewGuid());

        await using var connection = await _dataSource.OpenConnectionAsync();
        var raw = () => connection.ExecuteAsync(
            """
            INSERT INTO ciir_documents (project_id, ciir_id, schema_version, kind, language, content)
            VALUES (@ProjectId, @CiirId, '1.0', 'method', 'csharp', '{}'::jsonb)
            """,
            new { ProjectId = project.Id, CiirId = document.CiirId.Value });

        await Should.ThrowAsync<PostgresException>(raw);
    }

    [Fact]
    public async Task Ciir_relations_DeletingReferencedDocument_CascadesToDeleteTheRelation()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, EmbeddingModel.Create("bge-m3", PostgreSqlFixture.EmbeddingDimensions).Value);
        var source = TestData.BuildDocument();
        var target = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(target)], project.Id, Guid.NewGuid());
        var relation = TestData.BuildRelation(source.CiirId, targetCiirId: target.CiirId);
        await _relationWriter.UpsertBatchAsync([relation], project.Id, Guid.NewGuid());
        await _relationResolver.ResolveAsync(project.Id, [project.Id]);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM ciir_documents WHERE project_id = @ProjectId AND ciir_id = @CiirId",
            new { ProjectId = project.Id, CiirId = target.CiirId.Value });

        var remaining = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ciir_relations WHERE project_id = @ProjectId",
            new { ProjectId = project.Id });
        remaining.ShouldBe(0);
    }
}
