using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>Replaces every field of an existing project (full PUT replace, not a partial patch).</summary>
public sealed class UpdateProject
{
    private readonly IProjectStore _projectStore;

    public UpdateProject(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public async Task<Result<Project>> ExecuteAsync(
        long id,
        string? name,
        string? embeddingModel,
        int? embeddingDimensions,
        string? gitUrl,
        string? gitRawUrl,
        CancellationToken cancellationToken = default)
    {
        var validation = ProjectValidation.ValidateFields(name, embeddingModel, embeddingDimensions);
        if (validation is not null)
        {
            return validation;
        }

        var existing = await _projectStore.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ProjectFailures.ProjectNotFound(id);
        }

        if (await _projectStore.ExistsByNameAsync(name!, excludingId: id, cancellationToken))
        {
            return ProjectFailures.NameConflict(name!);
        }

        var updated = await _projectStore.UpdateAsync(
            id,
            name!,
            gitUrl,
            gitRawUrl,
            EmbeddingModel.Create(embeddingModel!, embeddingDimensions!.Value).Value,
            cancellationToken);

        return updated is null
            ? ProjectFailures.ProjectNotFound(id)
            : updated;
    }
}
