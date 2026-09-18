namespace Ciir.Indexer.Api.Contracts;

/// <summary>A single page of a paginated project search, plus the metadata needed to fetch the next one.</summary>
/// <param name="Items">The projects on this page.</param>
/// <param name="Page">The zero-based page number this response corresponds to.</param>
/// <param name="PageSize">The maximum number of items per page.</param>
/// <param name="TotalCount">The total number of projects matching the filter, across every page.</param>
/// <param name="TotalPages">The total number of pages available for the filter, given <paramref name="PageSize"/>.</param>
public sealed record ProjectListResponse(
    IReadOnlyList<ProjectResponse> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages);
