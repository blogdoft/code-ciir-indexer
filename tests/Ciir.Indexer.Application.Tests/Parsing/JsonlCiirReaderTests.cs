using Ciir.Indexer.Application.Parsing;
using Ciir.Indexer.Core;
using Shouldly;
using System.Security.Cryptography;
using System.Text;

namespace Ciir.Indexer.Application.Tests.Parsing;

public sealed class JsonlCiirReaderTests : IDisposable
{
    private readonly JsonlCiirReader _sut = new();
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task ReadAsync_ValidRecordWithRelations_ParsesDocumentAndRelationsCorrectly()
    {
        var docId = Sha256Of("A");
        var targetId = Sha256Of("B");
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"method","language":"csharp","project":"MyProj","symbol":{"name":"Foo","qualifiedName":"NS.Type.Foo","canonicalName":"NS.Type.Foo()"},"source":{"path":"src/Type.cs"},"embeddingText":"Entity: method","embeddingTextStrategy":"semantic-v1","embeddingTextHash":"<TEXT_HASH>","relations":[{"kind":"calls","target":{"id":"<TARGET_ID>","symbol":"NS.Other.Bar"},"resolution":{"status":"resolved","origin":"project"},"location":{"startLine":10,"startColumn":5,"endLine":10,"endColumn":20}}]}
            """
            .Replace("<DOC_ID>", docId)
            .Replace("<TARGET_ID>", targetId)
            .Replace("<TEXT_HASH>", Sha256Of("embedding"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        results.Count.ShouldBe(1);
        var success = results[0].ShouldBeOfType<CiirRecordReadResult.Success>();
        var record = success.Record;

        record.ProjectName.ShouldBe("MyProj");
        record.Document.CiirId.Value.ShouldBe(docId);
        record.Document.Kind.ShouldBe("method");
        record.Document.Language.ShouldBe("csharp");
        record.Document.Symbol.QualifiedName.ShouldBe("NS.Type.Foo");
        record.Document.SourcePath.ShouldBe("src/Type.cs");
        record.Document.EmbeddingText.ShouldBe("Entity: method");
        record.Document.RawContent.ShouldBe(jsonl);

        record.Relations.Count.ShouldBe(1);
        var relation = record.Relations[0];
        relation.SourceCiirId.Value.ShouldBe(docId);
        relation.TargetCiirId!.Value.Value.ShouldBe(targetId);
        relation.Kind.ShouldBe("calls");
        relation.TargetSymbol.ShouldBe("NS.Other.Bar");
        relation.Resolution.Status.ShouldBe(RelationResolutionStatus.Resolved);
        relation.Resolution.Origin.ShouldBe(RelationResolutionOrigin.Project);
        relation.SourcePath.ShouldBe("src/Type.cs");
        relation.StartLine.ShouldBe(10);
        relation.EndColumn.ShouldBe(20);
    }

    [Fact]
    public async Task ReadAsync_RelationWithoutTargetId_LeavesTargetCiirIdNull()
    {
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"method","language":"csharp","project":"MyProj","symbol":{"name":"Foo","qualifiedName":"NS.Type.Foo","canonicalName":"NS.Type.Foo()"},"relations":[{"kind":"calls","target":{"symbol":"System.String.IsNullOrEmpty"},"resolution":{"status":"external","origin":"framework"}}]}
            """
            .Replace("<DOC_ID>", Sha256Of("A"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        var relation = results[0].ShouldBeOfType<CiirRecordReadResult.Success>().Record.Relations[0];
        relation.TargetCiirId.ShouldBeNull();
        relation.TargetSymbol.ShouldBe("System.String.IsNullOrEmpty");
        relation.Resolution.Status.ShouldBe(RelationResolutionStatus.External);
        relation.Resolution.Origin.ShouldBe(RelationResolutionOrigin.Framework);
    }

    [Fact]
    public async Task ReadAsync_RelationWithSolutionOrigin_ParsesOriginCorrectly()
    {
        // "solution" (target in a different project of the same analyzed solution) is a valid
        // resolution.origin per the CIIR schema's enum, distinct from "project" (same project).
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"method","language":"csharp","project":"MyProj","symbol":{"name":"Foo","qualifiedName":"NS.Type.Foo","canonicalName":"NS.Type.Foo()"},"relations":[{"kind":"calls","target":{"symbol":"Other.Project.Bar"},"resolution":{"status":"resolved","origin":"solution"}}]}
            """
            .Replace("<DOC_ID>", Sha256Of("A"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        var relation = results[0].ShouldBeOfType<CiirRecordReadResult.Success>().Record.Relations[0];
        relation.Resolution.Status.ShouldBe(RelationResolutionStatus.Resolved);
        relation.Resolution.Origin.ShouldBe(RelationResolutionOrigin.Solution);
    }

    [Fact]
    public async Task ReadAsync_CiirId_IsPreservedIntegrally()
    {
        var docId = Sha256Of("some-very-specific-symbol-identity");
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T","qualifiedName":"NS.T","canonicalName":"NS.T"}}
            """
            .Replace("<DOC_ID>", docId);
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        results[0].ShouldBeOfType<CiirRecordReadResult.Success>().Record.Document.CiirId.Value.ShouldBe(docId);
    }

    [Fact]
    public async Task ReadAsync_MalformedJsonLine_YieldsErrorAndContinuesWithSubsequentValidLines()
    {
        var validId = Sha256Of("valid");
        var validLine = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T","qualifiedName":"NS.T","canonicalName":"NS.T"}}
            """
            .Replace("<DOC_ID>", validId);
        var jsonl = "{ this is not valid json" + Environment.NewLine + validLine;
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        results.Count.ShouldBe(2);
        var error = results[0].ShouldBeOfType<CiirRecordReadResult.Error>();
        error.LineNumber.ShouldBe(1);
        error.Category.ShouldBe(CiirRecordErrorCategory.InvalidJson);

        results[1].ShouldBeOfType<CiirRecordReadResult.Success>().Record.Document.CiirId.Value.ShouldBe(validId);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedSchemaVersion_YieldsErrorWithoutThrowing()
    {
        var jsonl = """
            {"schemaVersion":"99.0","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T","qualifiedName":"NS.T","canonicalName":"NS.T"}}
            """
            .Replace("<DOC_ID>", Sha256Of("x"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        var error = results[0].ShouldBeOfType<CiirRecordReadResult.Error>();
        error.Category.ShouldBe(CiirRecordErrorCategory.UnsupportedSchemaVersion);
    }

    [Fact]
    public async Task ReadAsync_MinorSchemaVersionBumpWithinSupportedMajor_IsAcceptedAsBackwardCompatible()
    {
        // A generator moving from "1.0" to "1.1" is a backward-compatible schema evolution (spec
        // §13/§44) - it must not require an indexer update to keep importing.
        var jsonl = """
            {"schemaVersion":"1.1","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T","qualifiedName":"NS.T","canonicalName":"NS.T"}}
            """
            .Replace("<DOC_ID>", Sha256Of("x"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        results[0].ShouldBeOfType<CiirRecordReadResult.Success>();
    }

    [Fact]
    public async Task ReadAsync_RecordMissingRequiredSymbolFields_YieldsInvalidRecordError()
    {
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T"}}
            """
            .Replace("<DOC_ID>", Sha256Of("x"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        var error = results[0].ShouldBeOfType<CiirRecordReadResult.Error>();
        error.Category.ShouldBe(CiirRecordErrorCategory.InvalidRecord);
    }

    [Fact]
    public async Task ReadAsync_BlankLines_AreSkippedWithoutProducingAResult()
    {
        var validLine = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"type","language":"csharp","project":"P","symbol":{"name":"T","qualifiedName":"NS.T","canonicalName":"NS.T"}}
            """
            .Replace("<DOC_ID>", Sha256Of("x"));
        var jsonl = Environment.NewLine + validLine + Environment.NewLine + Environment.NewLine;
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_DocumentWithoutEmbeddingText_LeavesEmbeddingFieldsNull()
    {
        var jsonl = """
            {"schemaVersion":"1.0","id":"<DOC_ID>","kind":"namespace","language":"csharp","project":"P","symbol":{"name":"NS","qualifiedName":"NS","canonicalName":"NS"}}
            """
            .Replace("<DOC_ID>", Sha256Of("x"));
        var path = WriteTempFile(jsonl);

        var results = await CollectAsync(path);

        var document = results[0].ShouldBeOfType<CiirRecordReadResult.Success>().Record.Document;
        document.EmbeddingText.ShouldBeNull();
        document.EmbeddingTextHash.ShouldBeNull();
    }

    private static string Sha256Of(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private async Task<List<CiirRecordReadResult>> CollectAsync(string path)
    {
        var results = new List<CiirRecordReadResult>();
        await foreach (var result in _sut.ReadAsync(path))
        {
            results.Add(result);
        }

        return results;
    }

    private string WriteTempFile(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
