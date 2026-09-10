using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public sealed class CiirDocumentWriterTests
{
    private readonly ICiirDocumentWriter _sut;
    private readonly IProjectStore _projectStore;
    private readonly NpgsqlDataSource _dataSource;

    public CiirDocumentWriterTests(PostgreSqlFixture fixture)
    {
        _sut = fixture.Services.GetRequiredService<ICiirDocumentWriter>();
        _projectStore = fixture.Services.GetRequiredService<IProjectStore>();
        _dataSource = fixture.GetDataSource();
    }

    [Fact]
    public async Task UpsertBatchAsync_NewDocumentWithEmbedding_PersistsVectorAndFingerprint()
    {
        var projectId = await CreateProjectAsync();
        var runId = Guid.NewGuid();
        var vector = TestData.RandomVector(PostgreSqlFixture.EmbeddingDimensions);
        var upsert = TestData.BuildUpsert(
            embedding: vector, embeddingModel: "bge-m3", embeddingDimensions: 4, embeddingFingerprintHash: "sha256:abc");

        await _sut.UpsertBatchAsync([upsert], projectId, runId);

        var stored = await GetStoredDocumentAsync(projectId, upsert.Document.CiirId);
        stored.ShouldNotBeNull();
        stored.EmbeddingFingerprintHash.ShouldBe("sha256:abc");
        stored.HasEmbedding.ShouldBeTrue();
    }

    [Fact]
    public async Task UpsertBatchAsync_DocumentWithoutEmbeddingText_LeavesEmbeddingNull()
    {
        var projectId = await CreateProjectAsync();
        var upsert = TestData.BuildUpsert();

        await _sut.UpsertBatchAsync([upsert], projectId, Guid.NewGuid());

        var stored = await GetStoredDocumentAsync(projectId, upsert.Document.CiirId);
        stored.ShouldNotBeNull();
        stored.HasEmbedding.ShouldBeFalse();
        stored.EmbeddingFingerprintHash.ShouldBeNull();
    }

    [Fact]
    public async Task UpsertBatchAsync_SameDocumentTwice_IsIdempotent()
    {
        var projectId = await CreateProjectAsync();
        var document = TestData.BuildDocument();
        var upsert = TestData.BuildUpsert(document);

        await _sut.UpsertBatchAsync([upsert], projectId, Guid.NewGuid());
        await _sut.UpsertBatchAsync([upsert], projectId, Guid.NewGuid());

        var count = await CountDocumentsAsync(projectId, document.CiirId);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task UpsertBatchAsync_ReuseWithNullEmbedding_PreservesThePreviouslyStoredVector()
    {
        var projectId = await CreateProjectAsync();
        var document = TestData.BuildDocument();
        var vector = TestData.RandomVector(PostgreSqlFixture.EmbeddingDimensions);
        var firstUpsert = TestData.BuildUpsert(
            document, embedding: vector, embeddingModel: "bge-m3", embeddingDimensions: 4, embeddingFingerprintHash: "sha256:v1");
        await _sut.UpsertBatchAsync([firstUpsert], projectId, Guid.NewGuid());

        // second run: fingerprint unchanged, so the caller passes Embedding = null + ReuseExistingEmbedding = true
        var reuseUpsert = TestData.BuildUpsert(
            document,
            embedding: null,
            reuseExistingEmbedding: true,
            embeddingModel: "bge-m3",
            embeddingDimensions: 4,
            embeddingFingerprintHash: "sha256:v1");
        await _sut.UpsertBatchAsync([reuseUpsert], projectId, Guid.NewGuid());

        var stored = await GetStoredDocumentAsync(projectId, document.CiirId);
        stored.ShouldNotBeNull();
        stored.HasEmbedding.ShouldBeTrue();
    }

    [Fact]
    public async Task UpsertBatchAsync_EmbeddingTextRemoved_ClearsThePreviouslyStoredVector()
    {
        // Distinguishes "reuse" (Embedding=null, ReuseExistingEmbedding=true) from "there is
        // genuinely no vector anymore" (Embedding=null, ReuseExistingEmbedding=false) - a document
        // whose embeddingText disappeared entirely must not keep a stale vector around.
        var projectId = await CreateProjectAsync();
        var document = TestData.BuildDocument();
        var vector = TestData.RandomVector(PostgreSqlFixture.EmbeddingDimensions);
        var firstUpsert = TestData.BuildUpsert(
            document, embedding: vector, embeddingModel: "bge-m3", embeddingDimensions: 4, embeddingFingerprintHash: "sha256:v1");
        await _sut.UpsertBatchAsync([firstUpsert], projectId, Guid.NewGuid());

        var documentWithoutEmbeddingText = TestData.BuildDocument(document.CiirId);
        var clearUpsert = TestData.BuildUpsert(documentWithoutEmbeddingText, embedding: null, reuseExistingEmbedding: false);
        await _sut.UpsertBatchAsync([clearUpsert], projectId, Guid.NewGuid());

        var stored = await GetStoredDocumentAsync(projectId, document.CiirId);
        stored.ShouldNotBeNull();
        stored.HasEmbedding.ShouldBeFalse();
    }

    [Fact]
    public async Task GetExistingFingerprintsAsync_KnownAndUnknownCiirIds_ReturnsOnlyKnownOnes()
    {
        var projectId = await CreateProjectAsync();
        var upsert = TestData.BuildUpsert(embeddingFingerprintHash: "sha256:known", embeddingModel: "m", embeddingDimensions: 4);
        await _sut.UpsertBatchAsync([upsert], projectId, Guid.NewGuid());
        var unknownId = TestData.NewCiirId();

        var result = await _sut.GetExistingFingerprintsAsync(
            projectId, [upsert.Document.CiirId.Value, unknownId.Value]);

        result[upsert.Document.CiirId.Value].ShouldBe("sha256:known");
        result.ContainsKey(unknownId.Value).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteStaleAsync_DocumentFromOlderRun_IsRemoved()
    {
        var projectId = await CreateProjectAsync();
        var staleRunId = Guid.NewGuid();
        var currentRunId = Guid.NewGuid();
        var staleDocument = TestData.BuildUpsert();
        var freshDocument = TestData.BuildUpsert();
        await _sut.UpsertBatchAsync([staleDocument], projectId, staleRunId);
        await _sut.UpsertBatchAsync([freshDocument], projectId, currentRunId);

        var deleted = await _sut.DeleteStaleAsync(projectId, currentRunId);

        deleted.ShouldBe(1);
        (await CountDocumentsAsync(projectId, staleDocument.Document.CiirId)).ShouldBe(0);
        (await CountDocumentsAsync(projectId, freshDocument.Document.CiirId)).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteStaleAsync_NoStaleDocuments_DeletesNothing()
    {
        var projectId = await CreateProjectAsync();
        var runId = Guid.NewGuid();
        await _sut.UpsertBatchAsync([TestData.BuildUpsert()], projectId, runId);

        var deleted = await _sut.DeleteStaleAsync(projectId, runId);

        deleted.ShouldBe(0);
    }

    private async Task<long> CreateProjectAsync()
    {
        var project = await _projectStore.EnsureProjectAsync(
            TestData.NewProjectName(), null, null, new EmbeddingModel("bge-m3", PostgreSqlFixture.EmbeddingDimensions));
        return project.Id;
    }

    private async Task<int> CountDocumentsAsync(long projectId, CiirIdentity ciirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ciir_documents WHERE project_id = @ProjectId AND ciir_id = @CiirId",
            new { ProjectId = projectId, CiirId = ciirId.Value });
    }

    private async Task<StoredDocument?> GetStoredDocumentAsync(long projectId, CiirIdentity ciirId)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<StoredDocument?>(
            """
            SELECT embedding_fingerprint_hash AS "EmbeddingFingerprintHash", (embedding IS NOT NULL) AS "HasEmbedding"
            FROM ciir_documents WHERE project_id = @ProjectId AND ciir_id = @CiirId
            """,
            new { ProjectId = projectId, CiirId = ciirId.Value });
    }

    private sealed record StoredDocument(string? EmbeddingFingerprintHash, bool HasEmbedding);
}
