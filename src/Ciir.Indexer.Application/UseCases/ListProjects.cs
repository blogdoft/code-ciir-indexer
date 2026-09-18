using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;

namespace Ciir.Indexer.Application.UseCases;

/// <summary>Validates and executes a paginated, optionally name-filtered search over <see cref="Project"/>.</summary>
public sealed class ListProjects
{
    private readonly IProjectStore _projectStore;

    public ListProjects(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public async Task<Result<ProjectPage>> ExecuteAsync(
        string? nameFilter,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken = default)
    {
        if (nameFilter is not null)
        {
            if (string.IsNullOrWhiteSpace(nameFilter))
            {
                return ProjectFailures.NameFilterEmpty();
            }

            if (nameFilter.Length > ProjectValidation.MaxNameFilterLength)
            {
                return ProjectFailures.NameFilterTooLong(ProjectValidation.MaxNameFilterLength);
            }
        }

        var resolvedPage = page ?? 0;
        if (resolvedPage < 0)
        {
            return ProjectFailures.PageInvalid();
        }

        var resolvedPageSize = pageSize ?? ProjectValidation.DefaultPageSize;
        if (resolvedPageSize < 1 || resolvedPageSize > ProjectValidation.MaxPageSize)
        {
            return ProjectFailures.PageSizeInvalid(ProjectValidation.MaxPageSize);
        }

        var (items, totalCount) = await _projectStore.SearchAsync(nameFilter, resolvedPage, resolvedPageSize, cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);

        return new ProjectPage(items, resolvedPage, resolvedPageSize, totalCount, totalPages);
    }
}
