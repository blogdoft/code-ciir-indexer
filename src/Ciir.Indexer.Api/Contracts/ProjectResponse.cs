namespace Ciir.Indexer.Api.Contracts;

/// <summary>A project stored by this service, either resolved from an indexation run or managed directly through this CRUD API.</summary>
/// <param name="Id">The project's id.</param>
/// <param name="Name">The project's name, unique across every project.</param>
/// <param name="EmbeddingModel">The embedding model this project's code documents are (or will be) embedded with.</param>
/// <param name="EmbeddingDimensions">The dimensionality of that embedding model's vectors.</param>
/// <param name="GitUrl">The project's public git repository URL, if any.</param>
/// <param name="GitRawUrl">The project's public git raw-content URL, if any.</param>
/// <param name="CreatedAt">When the project was first created.</param>
/// <param name="UpdatedAt">When the project was last updated.</param>
public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string? GitUrl,
    string? GitRawUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);
