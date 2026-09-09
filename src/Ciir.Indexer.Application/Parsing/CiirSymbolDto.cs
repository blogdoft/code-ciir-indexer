using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

internal sealed class CiirSymbolDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("qualifiedName")]
    public string QualifiedName { get; set; } = string.Empty;

    [JsonPropertyName("canonicalName")]
    public string CanonicalName { get; set; } = string.Empty;

    [JsonPropertyName("container")]
    public string? Container { get; set; }
}
