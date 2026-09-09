using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ciir.Indexer.Api.OpenApi;

/// <summary>
/// Sets the OpenAPI document's title and <c>info.description</c> to a whole-API purpose statement.
/// There is no XML doc anchor for "the document as a whole" (only for types/members), so - matching
/// the <c>code-rag-api</c> sibling repo's own precedent - this text is a plain literal here rather
/// than sourced from a comment.
/// </summary>
internal sealed class ApiInfoDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string ApiDescription = """
        Imports CIIR (Code Intelligence Intermediate Representation) JSONL files - produced by the
        `code-csharp-ciir` analyzer - into PostgreSQL with pgvector. For each CIIR document, this
        service generates an embedding from its precomputed `embeddingText` (reusing the existing
        stored vector whenever the text and embedding model are unchanged) and upserts the document,
        its relations (e.g. `calls`, `inherits`), and the resolved call/dependency graph.
        Re-importing the same file is safe and idempotent: unchanged data is left untouched, and
        only a fully successful run removes documents/relations that disappeared from the file.

        This service does not parse or analyze source code itself (that is `code-csharp-ciir`'s
        job), and it does not implement search, retrieval, reranking, or answer natural-language
        questions about the indexed code (that is `code-rag-api`'s job) - its only job is: consume
        CIIR documents, generate embeddings, and persist.

        Every endpoint accepts and returns `application/json`. Client and server errors are
        reported using the RFC 7807 "Problem Details for HTTP APIs" format
        (`application/problem+json`), with the exception of 404 responses, which carry no body.
        """;

    /// <summary>Sets <paramref name="document"/>'s title and description.</summary>
    /// <param name="document">The OpenAPI document being built.</param>
    /// <param name="context">Unused - the description is a fixed literal, not sourced from the request context.</param>
    /// <param name="cancellationToken">Unused - this transformer does no asynchronous work.</param>
    /// <returns>A completed task; this transformer mutates <paramref name="document"/> synchronously.</returns>
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "CIIR Indexer API";
        document.Info.Description = ApiDescription;

        return Task.CompletedTask;
    }
}
