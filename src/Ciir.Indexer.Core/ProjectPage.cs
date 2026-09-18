namespace Ciir.Indexer.Core;

/// <summary>One page of a paginated <see cref="Project"/> search, plus the metadata needed to fetch the next one.</summary>
public sealed record ProjectPage(
    IReadOnlyList<Project> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages);
