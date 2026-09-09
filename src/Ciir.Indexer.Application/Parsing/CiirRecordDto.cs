using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

/// <summary>
/// A partial deserialization of one CIIR JSONL line: only the fields the indexer actually reads
/// (identity, symbol, embedding projection, relations). Unknown/unused CIIR properties -
/// documentation, conditions, controlFlow, kind-specific detail blocks, extensions, etc. - are
/// intentionally left out of this model; they still reach persistence verbatim via the original
/// line text stored in <c>content jsonb</c> (spec §12/§13), not through this DTO.
/// </summary>
internal sealed class CiirRecordDto
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("project")]
    public string Project { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public CiirSymbolDto? Symbol { get; set; }

    [JsonPropertyName("source")]
    public CiirSourceLocationDto? Source { get; set; }

    [JsonPropertyName("relations")]
    public List<CiirRelationDto>? Relations { get; set; }

    [JsonPropertyName("embeddingText")]
    public string? EmbeddingText { get; set; }

    [JsonPropertyName("embeddingTextStrategy")]
    public string? EmbeddingTextStrategy { get; set; }

    [JsonPropertyName("embeddingTextHash")]
    public string? EmbeddingTextHash { get; set; }
}
