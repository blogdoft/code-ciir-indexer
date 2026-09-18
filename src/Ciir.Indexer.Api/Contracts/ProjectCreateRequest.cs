namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/projects</c>.</summary>
/// <param name="Name">Required. The project's name. Must not be empty or blank, and must not already be used by another project (409 otherwise).</param>
/// <param name="EmbeddingModel">Required. The embedding model this project's code documents are (or will be) embedded with. Must not be empty or blank.</param>
/// <param name="EmbeddingDimensions">Required. The dimensionality of that embedding model's vectors. Must be a positive integer.</param>
/// <param name="GitUrl">Optional. The project's public git repository URL.</param>
/// <param name="GitRawUrl">Optional. The project's public git raw-content URL.</param>
public sealed record ProjectCreateRequest(
    string? Name,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    string? GitUrl = null,
    string? GitRawUrl = null);
