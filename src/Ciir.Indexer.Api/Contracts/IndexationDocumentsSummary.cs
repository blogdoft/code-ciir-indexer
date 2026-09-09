namespace Ciir.Indexer.Api.Contracts;

/// <summary>The <c>documents</c> section of <c>GET /api/indexations/{id}</c> (spec §41).</summary>
/// <param name="Processed">Total number of CIIR document records read from the file so far.</param>
/// <param name="Inserted">Of <paramref name="Processed"/>, how many were brand new (no prior row for that <c>ciir_id</c> in this project).</param>
/// <param name="Updated">Of <paramref name="Processed"/>, how many already existed and were upserted.</param>
/// <param name="EmbeddingsGenerated">
/// How many documents needed a new embedding call - either newly seen, or their <c>embeddingText</c>
/// changed since the last successful run.
/// </param>
/// <param name="EmbeddingsReused">
/// How many documents kept their previously stored embedding vector because neither the
/// <c>embeddingText</c> hash nor the embedding model/dimensions changed - no embedding call was made
/// for these.
/// </param>
public sealed record IndexationDocumentsSummary(
    long Processed, long Inserted, long Updated, long EmbeddingsGenerated, long EmbeddingsReused);
