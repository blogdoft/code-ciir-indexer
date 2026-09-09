using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

internal sealed class CiirRelationDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public CiirRelationTargetDto? Target { get; set; }

    [JsonPropertyName("resolution")]
    public CiirRelationResolutionDto? Resolution { get; set; }

    [JsonPropertyName("location")]
    public CiirRangeDto? Location { get; set; }
}
