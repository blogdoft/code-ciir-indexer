using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Permanently deletes a project. This does not delete the project's indexed CIIR
/// documents/relations - those remain, pointing at a project id that no longer exists, unless the
/// caller cleans them up separately.
/// </summary>
public sealed class DeleteProject
{
    private readonly IProjectStore _projectStore;

    public DeleteProject(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public async Task<Result<bool>> ExecuteAsync(long id, CancellationToken cancellationToken = default)
    {
        var deleted = await _projectStore.DeleteAsync(id, cancellationToken);
        return deleted
            ? Result<bool>.FromSuccess(true)
            : ProjectFailures.ProjectNotFound(id);
    }
}
