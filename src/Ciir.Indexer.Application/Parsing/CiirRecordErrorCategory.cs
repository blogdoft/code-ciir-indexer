namespace Ciir.Indexer.Application.Parsing;

/// <summary>The kind of problem found while parsing one CIIR JSONL line (spec §43).</summary>
public enum CiirRecordErrorCategory
{
    /// <summary>The line is not valid JSON.</summary>
    InvalidJson,

    /// <summary>The record's <c>schemaVersion</c> is not one this indexer supports.</summary>
    UnsupportedSchemaVersion,

    /// <summary>The record is valid JSON but violates the CIIR contract (missing/malformed fields).</summary>
    InvalidRecord,
}
