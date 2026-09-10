using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Resolves and persists <see cref="Project"/> identity (spec §9/§11). <see cref="Project.Name"/>
/// is caller-supplied per <c>POST /api/indexations</c> request (spec's "Atualização — Identidade
/// de projeto informada pelo chamador") - never derived from a CIIR record's own <c>project</c>
/// field, which prevents unrelated repositories that happen to share an internal component name
/// from colliding on the same project row.
/// </summary>
public interface IProjectStore
{
    /// <summary>
    /// Creates the project if it does not exist yet, or updates its git metadata and embedding
    /// model/dimensions if they changed since the last run - a model change forces re-embedding of
    /// the whole project even when <c>embeddingTextHash</c> is unchanged (spec §55).
    /// </summary>
    /// <param name="name">The project's logical identity, supplied by the API caller.</param>
    /// <param name="gitUrl">The project's git repository URL, if supplied by the caller.</param>
    /// <param name="gitRawUrl">The project's git raw-content URL, if supplied by the caller.</param>
    /// <param name="embeddingModel">The embedding model/dimensions currently configured for it.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<Project> EnsureProjectAsync(
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default);
}
