using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>
/// Creates a project directly through the projects CRUD API - as opposed to
/// <see cref="IProjectStore.EnsureProjectAsync"/>'s upsert-by-name behavior (used implicitly by
/// the CIIR upload flow), a duplicate name here is a 409, not a silent update.
/// </summary>
public sealed class CreateProject
{
    private readonly IProjectStore _projectStore;

    public CreateProject(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public async Task<Result<Project>> ExecuteAsync(
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

        if (await _projectStore.ExistsByNameAsync(name!, excludingId: null, cancellationToken))
        {
            return ProjectFailures.NameConflict(name!);
        }

        return await _projectStore.InsertAsync(
            name!,
            gitUrl,
            gitRawUrl,
            new EmbeddingModel(embeddingModel!, embeddingDimensions!.Value),
            cancellationToken);
    }
}
