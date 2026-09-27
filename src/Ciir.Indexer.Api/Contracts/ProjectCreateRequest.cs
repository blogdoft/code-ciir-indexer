namespace Ciir.Indexer.Api.Contracts;

/// <summary>Request body for <c>POST /api/projects</c>.</summary>
/// <param name="Name">Required. The project's name. Must not be empty or blank, and must not already be used by another project (409 otherwise).</param>
/// <param name="GitUrl">Optional. The project's public git repository URL.</param>
/// <param name="GitRawUrl">Optional. The project's public git raw-content URL.</param>
public sealed record ProjectCreateRequest(
    string? Name,
    string? GitUrl = null,
    string? GitRawUrl = null);
