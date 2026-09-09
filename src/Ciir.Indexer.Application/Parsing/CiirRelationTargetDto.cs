using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

internal sealed class CiirRelationTargetDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;
}
