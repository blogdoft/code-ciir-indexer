using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

internal sealed class CiirSourceLocationDto
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}
