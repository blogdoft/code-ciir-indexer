namespace Ciir.Indexer.Core;

/// <summary>
/// The logical grouping of CIIR documents (spec §9/§11). <see cref="Name"/> is the v1 logical
/// identity; <see cref="EmbeddingModel"/> records which embedding configuration is currently
/// authoritative for this project's vectors.
/// </summary>
public sealed record Project
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public required EmbeddingModel EmbeddingModel { get; init; }
}
