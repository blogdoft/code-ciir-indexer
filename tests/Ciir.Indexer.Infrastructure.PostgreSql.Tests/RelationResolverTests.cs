using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class RelationResolverTests
{
    private readonly IRelationResolver _sut;
    private readonly IProjectStore _projectStore;
    private readonly ICiirDocumentWriter _documentWriter;
    private readonly ICiirRelationWriter _relationWriter;
    private readonly NpgsqlDataSource _dataSource;

    public RelationResolverTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<IRelationResolver>();
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
        _documentWriter = fixture.Services.GetRequiredService<ICiirDocumentWriter>();
        _relationWriter = fixture.Services.GetRequiredService<ICiirRelationWriter>();
        _dataSource = fixture.GetDataSource();
    }

    [Fact]
    public async Task ResolveAsync_SourceCiirIdMatchesAnExistingDocument_ResolvesSourceDocumentId()
    {
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(source)], projectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(source.CiirId, targetCiirId: TestData.NewCiirId());
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        var sourceDocId = await GetDocumentIdAsync(projectId, source.CiirId);
        (await GetSourceDocumentIdAsync(projectId, source.CiirId)).ShouldBe(sourceDocId);
    }

    [Fact]
    public async Task ResolveAsync_TargetCiirIdMatchesAnExistingDocument_ResolvesTargetDocumentId()
    {
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var target = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(target)], projectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(source.CiirId, targetCiirId: target.CiirId);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        var targetDocId = await GetDocumentIdAsync(projectId, target.CiirId);
        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBe(targetDocId);
    }

    [Fact]
    public async Task ResolveAsync_TargetCiirIdAbsentButSymbolMatchesExactlyOneDocument_ResolvesBySymbol()
    {
        // The common real-world case: the C# CIIR generator never populates relation.target.id, so
        // resolution must fall back to matching target_symbol against symbol_canonical_name.
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var target = TestData.BuildDocument(qualifiedName: "NS.Other.UniqueMember");
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(target)], projectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, targetSymbol: "NS.Other.UniqueMember", status: RelationResolutionStatus.Resolved);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        var targetDocId = await GetDocumentIdAsync(projectId, target.CiirId);
        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBe(targetDocId);

        // The symbol match also backfills target_ciir_id from the matched document, even though
        // CIIR itself never supplied relation.target.id - both identity forms should end up
        // consistent (spec §17).
        (await GetTargetCiirIdAsync(projectId, source.CiirId)).ShouldBe(target.CiirId.Value);
    }

    [Fact]
    public async Task ResolveAsync_TwoOverloadsShareQualifiedNameButDiffer_ResolvesToTheMatchingCanonicalOverload()
    {
        // Reproduces real CIIR generator output: relation.target.symbol carries the canonical
        // name (includes parameter types), while multiple overloads can share one bare
        // symbol_qualified_name. Resolution must join on symbol_canonical_name so the specific
        // overload is found, rather than treating this as an unresolvable ambiguity.
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var intOverload = TestData.BuildDocument(
            qualifiedName: "NS.Type.Overloaded", canonicalName: "NS.Type.Overloaded(System.Int32)");
        var stringOverload = TestData.BuildDocument(
            qualifiedName: "NS.Type.Overloaded", canonicalName: "NS.Type.Overloaded(System.String)");
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(intOverload), TestData.BuildUpsert(stringOverload)],
            projectId,
            Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId,
            targetCiirId: null,
            targetSymbol: "NS.Type.Overloaded(System.String)",
            status: RelationResolutionStatus.Resolved);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        var expectedDocId = await GetDocumentIdAsync(projectId, stringOverload.CiirId);
        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBe(expectedDocId);
        (await GetTargetCiirIdAsync(projectId, source.CiirId)).ShouldBe(stringOverload.CiirId.Value);
    }

    [Fact]
    public async Task ResolveAsync_SolutionOriginTargetLivesInADifferentProject_ResolvesAcrossProjects()
    {
        // resolution_origin "solution" (CIIR schemaVersion >= 1.1) means the target lives in a
        // different project of the same analyzed solution, not the relation's own project - the
        // symbol match must therefore search every project passed as allProjectIds, not just the
        // relation's own project.
        var sourceProjectId = await CreateProjectAsync();
        var targetProjectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var target = TestData.BuildDocument(qualifiedName: "Other.Project.UniqueMember");
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(source)], sourceProjectId, Guid.NewGuid());
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(target)], targetProjectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId,
            targetCiirId: null,
            targetSymbol: "Other.Project.UniqueMember",
            status: RelationResolutionStatus.Resolved,
            origin: RelationResolutionOrigin.Solution);
        await _relationWriter.UpsertBatchAsync([relation], sourceProjectId, Guid.NewGuid());

        await _sut.ResolveAsync(sourceProjectId, [sourceProjectId, targetProjectId]);

        var expectedDocId = await GetDocumentIdAsync(targetProjectId, target.CiirId);
        (await GetTargetDocumentIdAsync(sourceProjectId, source.CiirId)).ShouldBe(expectedDocId);
        (await GetTargetCiirIdAsync(sourceProjectId, source.CiirId)).ShouldBe(target.CiirId.Value);
    }

    [Fact]
    public async Task ResolveAsync_ProjectOriginTarget_IsNeverSearchedAcrossOtherProjects()
    {
        // The opposite of the test above: a "project"-origin relation must stay scoped to its own
        // project even when allProjectIds includes others - a same-named document elsewhere in the
        // solution must never be used to resolve it.
        var sourceProjectId = await CreateProjectAsync();
        var otherProjectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var lookalike = TestData.BuildDocument(qualifiedName: "NS.Other.Member");
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(source)], sourceProjectId, Guid.NewGuid());
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(lookalike)], otherProjectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId,
            targetCiirId: null,
            targetSymbol: "NS.Other.Member",
            status: RelationResolutionStatus.Resolved,
            origin: RelationResolutionOrigin.Project);
        await _relationWriter.UpsertBatchAsync([relation], sourceProjectId, Guid.NewGuid());

        await _sut.ResolveAsync(sourceProjectId, [sourceProjectId, otherProjectId]);

        (await GetTargetDocumentIdAsync(sourceProjectId, source.CiirId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_SymbolMatchesTwoDocuments_LeavesTargetUnresolvedRatherThanGuessing()
    {
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var overload1 = TestData.BuildDocument(qualifiedName: "NS.Type.Overloaded");
        var overload2 = TestData.BuildDocument(qualifiedName: "NS.Type.Overloaded");
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(overload1), TestData.BuildUpsert(overload2)],
            projectId,
            Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, targetSymbol: "NS.Type.Overloaded", status: RelationResolutionStatus.Resolved);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBeNull();
        (await GetTargetCiirIdAsync(projectId, source.CiirId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_ExternalTargetWithSymbolThatWouldOtherwiseMatch_StaysUnresolved()
    {
        // Symbol-match resolution only applies to CIIR-classified "resolved" targets - it must
        // never override a status CIIR itself reported, even if the symbol happens to match a
        // document in this project (spec §46).
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var lookalike = TestData.BuildDocument(qualifiedName: "System.String.IsNullOrEmpty");
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(lookalike)], projectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId,
            targetCiirId: null,
            targetSymbol: "System.String.IsNullOrEmpty",
            status: RelationResolutionStatus.External,
            origin: RelationResolutionOrigin.Framework);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_GenuinelyMissingTarget_StaysUnresolved()
    {
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync([TestData.BuildUpsert(source)], projectId, Guid.NewGuid());
        var relation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, targetSymbol: "NS.Nowhere.Member", status: RelationResolutionStatus.Unresolved);
        await _relationWriter.UpsertBatchAsync([relation], projectId, Guid.NewGuid());

        await _sut.ResolveAsync(projectId, [projectId]);

        (await GetTargetDocumentIdAsync(projectId, source.CiirId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_MixOfRelationKinds_ReportsAccurateCounters()
    {
        var projectId = await CreateProjectAsync();
        var source = TestData.BuildDocument();
        var target = TestData.BuildDocument();
        await _documentWriter.UpsertBatchAsync(
            [TestData.BuildUpsert(source), TestData.BuildUpsert(target)], projectId, Guid.NewGuid());
        var resolvedRelation = TestData.BuildRelation(source.CiirId, targetCiirId: target.CiirId, startLine: 1);
        var externalRelation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, status: RelationResolutionStatus.External, startLine: 2);
        var unresolvedRelation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, status: RelationResolutionStatus.Unresolved, startLine: 3);
        var dynamicRelation = TestData.BuildRelation(
            source.CiirId, targetCiirId: null, status: RelationResolutionStatus.Dynamic, startLine: 4);
        await _relationWriter.UpsertBatchAsync(
            [resolvedRelation, externalRelation, unresolvedRelation, dynamicRelation], projectId, Guid.NewGuid());

        var counters = await _sut.ResolveAsync(projectId, [projectId]);

        counters.RelationsTotal.ShouldBe(4);
        counters.SourceResolved.ShouldBe(4);
        counters.TargetResolved.ShouldBe(1);
        counters.ExternalTargets.ShouldBe(1);
        counters.UnresolvedTargets.ShouldBe(1);
        counters.DynamicTargets.ShouldBe(1);
    }

    private async Task<long> CreateProjectAsync()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, EmbeddingModel.Create("bge-m3", PostgreSqlFixture.EmbeddingDimensions).Value);
        return project.Id;
    }

    private async Task<long> GetDocumentIdAsync(long projectId, CiirIdentity ciirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<long>(
            "SELECT id FROM ciir_documents WHERE project_id = @ProjectId AND ciir_id = @CiirId",
            new { ProjectId = projectId, CiirId = ciirId.Value });
    }

    private async Task<long?> GetSourceDocumentIdAsync(long projectId, CiirIdentity sourceCiirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<long?>(
            "SELECT source_document_id FROM ciir_relations WHERE project_id = @ProjectId AND source_ciir_id = @SourceCiirId",
            new { ProjectId = projectId, SourceCiirId = sourceCiirId.Value });
    }

    private async Task<long?> GetTargetDocumentIdAsync(long projectId, CiirIdentity sourceCiirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<long?>(
            "SELECT target_document_id FROM ciir_relations WHERE project_id = @ProjectId AND source_ciir_id = @SourceCiirId",
            new { ProjectId = projectId, SourceCiirId = sourceCiirId.Value });
    }

    private async Task<string?> GetTargetCiirIdAsync(long projectId, CiirIdentity sourceCiirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<string?>(
            "SELECT target_ciir_id FROM ciir_relations WHERE project_id = @ProjectId AND source_ciir_id = @SourceCiirId",
            new { ProjectId = projectId, SourceCiirId = sourceCiirId.Value });
    }
}
