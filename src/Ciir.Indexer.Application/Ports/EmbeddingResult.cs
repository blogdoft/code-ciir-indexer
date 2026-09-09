namespace Ciir.Indexer.Application.Ports;

/// <summary>One embedding vector plus the provenance needed to compute its fingerprint.</summary>
public sealed record EmbeddingResult(
    ReadOnlyMemory<float> Vector,
    string Provider,
    string Model,
    int Dimensions);
