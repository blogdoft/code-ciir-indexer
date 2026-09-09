using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Parsing;

/// <summary>
/// One successfully parsed CIIR JSONL line, exposing both the document view (for Document Import)
/// and the relation view (for Relation Import) of the same record. Each import use case reads only
/// the half it needs; both are cheap to build from the same already-deserialized line, so exposing
/// both here avoids parsing twice while each use case still performs its own independent pass over
/// the file, per spec §4.
/// </summary>
public sealed record CiirParsedRecord
{
    public required string ProjectName { get; init; }

    public required CiirDocument Document { get; init; }

    public required IReadOnlyList<CiirRelation> Relations { get; init; }
}
