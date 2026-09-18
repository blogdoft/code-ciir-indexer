namespace Ciir.Indexer.Api.Contracts;

/// <summary>
/// Request body for <c>PUT /api/projects/{projectId}</c>. Full replacement fields for an existing
/// project - this is a full PUT replace, not a partial patch, so every field must be supplied.
/// </summary>
/// <param name="Name">The project's new name. Must not be empty or blank, and must not already be used by another project (409 otherwise).</param>
/// <param name="EmbeddingModel">The project's new embedding model. Must not be empty or blank.</param>
/// <param name="EmbeddingDimensions">The project's new embedding dimensionality. Must be a positive integer.</param>
/// <param name="GitUrl">The project's public git repository URL.</param>
/// <param name="GitRawUrl">The project's public git raw-content URL.</param>
public sealed record ProjectUpdateRequest(
    string? Name,
    string? EmbeddingModel,
    int? EmbeddingDimensions,
    string? GitUrl = null,
    string? GitRawUrl = null);
