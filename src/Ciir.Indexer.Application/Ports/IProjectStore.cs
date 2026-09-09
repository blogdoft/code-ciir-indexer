using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Resolves and persists <see cref="Project"/> identity (spec §9/§11). <see cref="Project.Name"/>
/// is the v1 logical identity.
/// </summary>
public interface IProjectStore
{
    /// <summary>
    /// Creates the project if it does not exist yet, or updates its embedding model/dimensions if
    /// they changed since the last run - a model change forces re-embedding of the whole project
    /// even when <c>embeddingTextHash</c> is unchanged (spec §55).
    /// </summary>
    /// <param name="name">The project's logical identity.</param>
    /// <param name="embeddingModel">The embedding model/dimensions currently configured for it.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task<Project> EnsureProjectAsync(
        string name, EmbeddingModel embeddingModel, CancellationToken cancellationToken = default);
}
