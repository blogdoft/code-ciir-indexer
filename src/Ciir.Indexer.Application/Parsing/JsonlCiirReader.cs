using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Ciir.Indexer.Application.Parsing;

/// <inheritdoc cref="ICiirJsonlReader" />
public sealed class JsonlCiirReader : ICiirJsonlReader
{
    public async IAsyncEnumerable<CiirRecordReadResult> ReadAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return ParseLine(line, lineNumber);
        }
    }

    private static CiirRecordReadResult ParseLine(string line, int lineNumber)
    {
        CiirRecordDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(line, CiirJsonContext.Default.CiirRecordDto)
                ?? throw new JsonException("Line deserialized to null.");
        }
        catch (JsonException ex)
        {
            return new CiirRecordReadResult.Error(lineNumber, CiirRecordErrorCategory.InvalidJson, ex.Message);
        }

        if (!CiirSchemaVersions.IsSupported(dto.SchemaVersion))
        {
            return new CiirRecordReadResult.Error(
                lineNumber,
                CiirRecordErrorCategory.UnsupportedSchemaVersion,
                $"Unsupported CIIR schemaVersion '{dto.SchemaVersion}'. Supported major version(s): {string.Join(", ", CiirSchemaVersions.SupportedMajorVersions)}.");
        }

        try
        {
            return new CiirRecordReadResult.Success(MapToParsedRecord(dto, line));
        }
        catch (ArgumentException ex)
        {
            return new CiirRecordReadResult.Error(lineNumber, CiirRecordErrorCategory.InvalidRecord, ex.Message);
        }
    }

    private static CiirParsedRecord MapToParsedRecord(CiirRecordDto dto, string rawLine)
    {
        if (string.IsNullOrWhiteSpace(dto.Project))
        {
            throw new ArgumentException("CIIR record is missing required field 'project'.");
        }

        if (string.IsNullOrWhiteSpace(dto.Kind))
        {
            throw new ArgumentException("CIIR record is missing required field 'kind'.");
        }

        if (string.IsNullOrWhiteSpace(dto.Language))
        {
            throw new ArgumentException("CIIR record is missing required field 'language'.");
        }

        var symbol = MapSymbol(dto.Symbol);
        var ciirId = new CiirIdentity(dto.Id);
        var sourcePath = dto.Source?.Path;

        var document = new CiirDocument
        {
            CiirId = ciirId,
            SchemaVersion = dto.SchemaVersion,
            Kind = dto.Kind,
            Language = dto.Language,
            Symbol = symbol,
            SourcePath = sourcePath,
            EmbeddingText = dto.EmbeddingText,
            EmbeddingTextStrategy = dto.EmbeddingTextStrategy,
            EmbeddingTextHash = dto.EmbeddingTextHash is { Length: > 0 } hash ? new EmbeddingTextHash(hash) : null,
            RawContent = rawLine,
        };

        return new CiirParsedRecord
        {
            ProjectName = dto.Project,
            Document = document,
            Relations = MapRelations(dto.Relations, ciirId, sourcePath),
        };
    }

    private static CiirSymbol MapSymbol(CiirSymbolDto? dto)
    {
        if (dto is null
            || string.IsNullOrWhiteSpace(dto.Name)
            || string.IsNullOrWhiteSpace(dto.QualifiedName)
            || string.IsNullOrWhiteSpace(dto.CanonicalName))
        {
            throw new ArgumentException(
                "CIIR record is missing required 'symbol.name'/'symbol.qualifiedName'/'symbol.canonicalName'.");
        }

        return new CiirSymbol
        {
            Name = dto.Name,
            QualifiedName = dto.QualifiedName,
            CanonicalName = dto.CanonicalName,
            Container = dto.Container,
        };
    }

    private static IReadOnlyList<CiirRelation> MapRelations(
        List<CiirRelationDto>? dtos, CiirIdentity sourceCiirId, string? sourcePath)
    {
        if (dtos is null or { Count: 0 })
        {
            return [];
        }

        var relations = new List<CiirRelation>(dtos.Count);
        foreach (var relationDto in dtos)
        {
            relations.Add(MapRelation(relationDto, sourceCiirId, sourcePath));
        }

        return relations;
    }

    private static CiirRelation MapRelation(CiirRelationDto dto, CiirIdentity sourceCiirId, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(dto.Kind))
        {
            throw new ArgumentException("CIIR relation is missing required field 'kind'.");
        }

        if (dto.Target is null || string.IsNullOrWhiteSpace(dto.Target.Symbol))
        {
            throw new ArgumentException("CIIR relation is missing required field 'target.symbol'.");
        }

        if (dto.Resolution is null)
        {
            throw new ArgumentException("CIIR relation is missing required field 'resolution'.");
        }

        return new CiirRelation
        {
            SourceCiirId = sourceCiirId,
            TargetCiirId = dto.Target.Id is { Length: > 0 } targetId ? new CiirIdentity(targetId) : null,
            Kind = dto.Kind,
            TargetSymbol = dto.Target.Symbol,
            Resolution = MapResolution(dto.Resolution),
            SourcePath = sourcePath,
            StartLine = dto.Location?.StartLine,
            StartColumn = dto.Location?.StartColumn,
            EndLine = dto.Location?.EndLine,
            EndColumn = dto.Location?.EndColumn,
        };
    }

    private static CiirRelationResolution MapResolution(CiirRelationResolutionDto dto) => new()
    {
        Status = MapStatus(dto.Status),
        Origin = MapOrigin(dto.Origin),
        Reason = dto.Reason,
    };

    private static RelationResolutionStatus MapStatus(string status) => status switch
    {
        "resolved" => RelationResolutionStatus.Resolved,
        "unresolved" => RelationResolutionStatus.Unresolved,
        "ambiguous" => RelationResolutionStatus.Ambiguous,
        "external" => RelationResolutionStatus.External,
        "dynamic" => RelationResolutionStatus.Dynamic,
        _ => throw new ArgumentException($"Unrecognized relation resolution status '{status}'."),
    };

    private static RelationResolutionOrigin MapOrigin(string origin) => origin switch
    {
        "project" => RelationResolutionOrigin.Project,
        "solution" => RelationResolutionOrigin.Solution,
        "dependency" => RelationResolutionOrigin.Dependency,
        "framework" => RelationResolutionOrigin.Framework,
        "runtime" => RelationResolutionOrigin.Runtime,
        "external_service" => RelationResolutionOrigin.ExternalService,
        "unknown" => RelationResolutionOrigin.Unknown,
        _ => throw new ArgumentException($"Unrecognized relation resolution origin '{origin}'."),
    };
}
