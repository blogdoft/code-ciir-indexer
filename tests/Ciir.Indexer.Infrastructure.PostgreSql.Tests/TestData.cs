using Bogus;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Infrastructure.PostgreSql.Tests;

/// <summary>Builders for Core domain objects used across the PostgreSQL integration tests.</summary>
internal static class TestData
{
    private static readonly Faker Faker = new();

    public static string NewProjectName() => $"project-{Guid.NewGuid():N}";

    public static CiirIdentity NewCiirId() => Sha256Identity(Guid.NewGuid().ToString());

    public static CiirIdentity Sha256Identity(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return CiirIdentity.Create("sha256:" + Convert.ToHexStringLower(hash)).Value;
    }

    public static EmbeddingTextHash NewEmbeddingTextHash() =>
        EmbeddingTextHash.Create(
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())))).Value;

    public static CiirDocument BuildDocument(
        CiirIdentity? ciirId = null,
        string kind = "method",
        string? qualifiedName = null,
        string? canonicalName = null,
        string? embeddingText = null,
        EmbeddingTextHash? embeddingTextHash = null)
    {
        var name = Faker.Hacker.Noun();
        return new CiirDocument
        {
            CiirId = ciirId ?? NewCiirId(),
            SchemaVersion = "1.0",
            Kind = kind,
            Language = "csharp",
            Symbol = new CiirSymbol
            {
                Name = name,
                QualifiedName = qualifiedName ?? $"NS.Type.{name}",
                CanonicalName = canonicalName ?? qualifiedName ?? $"NS.Type.{name}()",
                Container = "NS.Type",
            },
            SourcePath = "src/Type.cs",
            EmbeddingText = embeddingText,
            EmbeddingTextStrategy = embeddingText is null ? null : "semantic-v1",
            EmbeddingTextHash = embeddingTextHash,
            RawContent = """{"kind":"method"}""",
        };
    }

    public static CiirDocumentUpsert BuildUpsert(
        CiirDocument? document = null,
        ReadOnlyMemory<float>? embedding = null,
        bool reuseExistingEmbedding = false,
        string? embeddingModel = null,
        int? embeddingDimensions = null,
        string? embeddingFingerprintHash = null)
    {
        return new CiirDocumentUpsert
        {
            Document = document ?? BuildDocument(),
            Embedding = embedding,
            ReuseExistingEmbedding = reuseExistingEmbedding,
            EmbeddingModel = embeddingModel,
            EmbeddingDimensions = embeddingDimensions,
            EmbeddingFingerprintHash = embeddingFingerprintHash,
        };
    }

    public static ReadOnlyMemory<float> RandomVector(int dimensions) =>
        Enumerable.Range(0, dimensions).Select(_ => (float)Faker.Random.Double(-1, 1)).ToArray();

    public static CiirRelation BuildRelation(
        CiirIdentity sourceCiirId,
        CiirIdentity? targetCiirId = null,
        string kind = "calls",
        string? targetSymbol = null,
        RelationResolutionStatus status = RelationResolutionStatus.Resolved,
        RelationResolutionOrigin origin = RelationResolutionOrigin.Project,
        int? startLine = null)
    {
        return new CiirRelation
        {
            SourceCiirId = sourceCiirId,
            TargetCiirId = targetCiirId,
            Kind = kind,
            TargetSymbol = targetSymbol ?? "NS.Other.Member",
            Resolution = new CiirRelationResolution { Status = status, Origin = origin },
            SourcePath = "src/Type.cs",
            StartLine = startLine ?? Faker.Random.Int(1, 1000),
            StartColumn = 1,
            EndLine = startLine ?? Faker.Random.Int(1, 1000),
            EndColumn = 10,
        };
    }
}
