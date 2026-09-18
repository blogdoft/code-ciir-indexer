namespace Ciir.Indexer.Core;

/// <summary>
/// The logical grouping of CIIR documents (spec §9/§11). <see cref="Name"/> is caller-supplied per
/// <c>POST /api/indexations</c> request, not derived from any CIIR record; <see
/// cref="EmbeddingModel"/> records which embedding configuration is currently authoritative for
/// this project's vectors.
/// </summary>
public sealed record Project
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public string? GitUrl { get; init; }

    public string? GitRawUrl { get; init; }

    public required EmbeddingModel EmbeddingModel { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}
