using System.Text.Json.Serialization;

namespace Ciir.Indexer.Application.Parsing;

/// <summary>
/// Source-generated serialization context for <see cref="CiirRecordDto"/>: avoids reflection-based
/// deserialization overhead on every JSONL line, which matters for the large-file streaming path
/// (spec §61).
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(CiirRecordDto))]
internal sealed partial class CiirJsonContext : JsonSerializerContext;
