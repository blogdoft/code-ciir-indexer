namespace Ciir.Indexer.Core;

/// <summary>
/// A single CIIR document (spec §12): the domain shape of one node in the persisted graph.
/// <see cref="CiirId"/> is the element's real, CIIR-native identity and is preserved integrally -
/// the indexer never generates a new semantic identifier for it. <see cref="RawContent"/> holds
/// the complete original CIIR record (destined for a <c>jsonb</c> column) so that properties not
/// promoted to their own field are never lost as the CIIR contract evolves (spec §13).
/// </summary>
public sealed record CiirDocument
{
    public required CiirIdentity CiirId { get; init; }

    public required string SchemaVersion { get; init; }

    public required string Kind { get; init; }

    public required string Language { get; init; }

    public required CiirSymbol Symbol { get; init; }

    public string? SourcePath { get; init; }

    /// <summary>
    /// The precomputed semantic projection used to generate the embedding. Absent for CIIR kinds
    /// that are not (yet) semantically indexable - in that case no embedding is ever invented
    /// (spec §45).
    /// </summary>
    public string? EmbeddingText { get; init; }

    public string? EmbeddingTextStrategy { get; init; }

    public EmbeddingTextHash? EmbeddingTextHash { get; init; }

    /// <summary>The complete original CIIR JSON record, verbatim.</summary>
    public required string RawContent { get; init; }
}
