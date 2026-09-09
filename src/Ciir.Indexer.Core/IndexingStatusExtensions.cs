namespace Ciir.Indexer.Core;

/// <summary>
/// The canonical lowercase/snake_case string vocabulary for <see cref="IndexingStatus"/> (spec §26),
/// shared by every layer that serializes it - the persisted <c>indexing_runs.status</c> column and
/// the <c>GET /api/indexations/{id}</c> response both use these exact tokens.
/// </summary>
public static class IndexingStatusExtensions
{
    public static string ToWireString(this IndexingStatus status) => status switch
    {
        IndexingStatus.Pending => "pending",
        IndexingStatus.Running => "running",
        IndexingStatus.ResolvingRelations => "resolving_relations",
        IndexingStatus.Completed => "completed",
        IndexingStatus.Failed => "failed",
        IndexingStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };

    public static IndexingStatus ParseIndexingStatus(string status) => status switch
    {
        "pending" => IndexingStatus.Pending,
        "running" => IndexingStatus.Running,
        "resolving_relations" => IndexingStatus.ResolvingRelations,
        "completed" => IndexingStatus.Completed,
        "failed" => IndexingStatus.Failed,
        "cancelled" => IndexingStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };
}
