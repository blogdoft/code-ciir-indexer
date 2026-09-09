namespace Ciir.Indexer.Api.Contracts;

/// <summary>The <c>relations</c> section of <c>GET /api/indexations/{id}</c> (spec §41).</summary>
/// <param name="Processed">Total number of relation edges (e.g. <c>calls</c>, <c>inherits</c>) read from the file so far.</param>
/// <param name="Resolved">
/// Of <paramref name="Processed"/>, how many now have their target document linked by foreign key -
/// these are the edges traversable in the call/dependency graph. Includes both same-project
/// resolution by <c>ciir_id</c> and the symbol-name fallback match.
/// </param>
/// <param name="Unresolved">
/// Of <paramref name="Processed"/>, how many CIIR itself classified as
/// <c>resolution.status = "unresolved"</c> - genuinely missing targets, not external references or
/// dynamic dispatch (those are expected and are not counted here as a problem).
/// </param>
public sealed record IndexationRelationsSummary(long Processed, long Resolved, long Unresolved);
