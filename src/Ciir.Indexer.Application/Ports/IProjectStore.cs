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

    /// <summary>
    /// Looks up an already-registered project by its internal id (an already-resolved FK value,
    /// e.g. <see cref="Core.CiirUpload.ProjectId"/>) - unlike <see cref="EnsureProjectAsync"/>, this
    /// never creates or updates anything. Never call this with a caller-supplied value; see
    /// <see cref="GetByPublicIdAsync"/> for that.
    /// </summary>
    /// <param name="id">The project's internal id to look up.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The project, or <c>null</c> if no project with this id exists.</returns>
    Task<Project?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an already-registered project by its public id - the identifier every API caller
    /// actually supplies (route parameters, <c>projectId</c> form/body fields).
    /// </summary>
    /// <param name="publicId">The project's public id to look up.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The project, or <c>null</c> if no project with this public id exists.</returns>
    Task<Project?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a page of projects whose name matches <paramref name="nameFilter"/> (partial,
    /// case-insensitive), or every project when <paramref name="nameFilter"/> is null, ordered by
    /// name, alongside the total number of matching rows across every page.
    /// </summary>
    /// <param name="nameFilter">Partial, case-insensitive name filter, or null to match every project.</param>
    /// <param name="page">Zero-based page number to retrieve.</param>
    /// <param name="pageSize">Maximum number of projects per page.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<(IReadOnlyList<Project> Items, long TotalCount)> SearchAsync(
        string? nameFilter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether a project named <paramref name="name"/> already exists, excluding the
    /// project identified by <paramref name="excludingId"/> (if given) from the check - used to
    /// allow an update to keep a project's own current name.
    /// </summary>
    /// <param name="name">The project name to look up.</param>
    /// <param name="excludingId">A project id to exclude from the check, if any.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<bool> ExistsByNameAsync(string name, long? excludingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new project directly (as opposed to <see cref="EnsureProjectAsync"/>'s
    /// upsert-by-name behavior) and returns the persisted record, including its generated id and
    /// timestamps - used by the projects CRUD API, where a duplicate name must fail rather than
    /// silently update the existing row.
    /// </summary>
    /// <param name="name">The new project's name.</param>
    /// <param name="gitUrl">The project's git repository URL, if supplied by the caller.</param>
    /// <param name="gitRawUrl">The project's git raw-content URL, if supplied by the caller.</param>
    /// <param name="embeddingModel">The embedding model/dimensions this project's documents are (or will be) embedded with.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<Project> InsertAsync(
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every field of the project identified by <paramref name="id"/> and returns the
    /// updated record, or null when no project exists with that id.
    /// </summary>
    /// <param name="id">The id of the project to update.</param>
    /// <param name="name">The project's new name.</param>
    /// <param name="gitUrl">The project's new git repository URL.</param>
    /// <param name="gitRawUrl">The project's new git raw-content URL.</param>
    /// <param name="embeddingModel">The project's new embedding model/dimensions.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<Project?> UpdateAsync(
        long id,
        string name,
        string? gitUrl,
        string? gitRawUrl,
        EmbeddingModel embeddingModel,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes the project identified by <paramref name="id"/>. Returns whether a row was deleted.</summary>
    /// <param name="id">The id of the project to delete.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
