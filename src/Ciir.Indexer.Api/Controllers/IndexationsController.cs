using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Api.Problems;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Application.UseCases;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Controllers;

/// <summary>
/// The API's only entry point (spec §2/§40): starts an indexation and reports its progress.
/// Contains no indexing logic of its own - it validates the request shape, delegates to
/// <see cref="StartIndexation"/> (the actual path validation/run creation logic lives there), and
/// maps the result to an HTTP response.
/// </summary>
// S6960: flags this controller for having both a POST and a GET action. That's the standard
// REST create+read pair for a single resource (start an indexation, check its status), not two
// responsibilities - splitting it into two controllers would fragment one resource's operations
// for no benefit.
#pragma warning disable S6960
[ApiController]
[Route("api/indexations")]
public sealed class IndexationsController : ControllerBase
{
    private readonly StartIndexation _startIndexation;
    private readonly IIndexingRunStore _runStore;

    /// <summary>Initializes a new instance of the <see cref="IndexationsController"/> class.</summary>
    /// <param name="startIndexation">Validates the request path and creates/enqueues the run.</param>
    /// <param name="runStore">Reads back a run's current status and counters for <see cref="GetAsync"/>.</param>
    public IndexationsController(StartIndexation startIndexation, IIndexingRunStore runStore)
    {
        _startIndexation = startIndexation;
        _runStore = runStore;
    }

    /// <summary>Starts a new indexation of the given CIIR JSONL file.</summary>
    /// <remarks>
    /// Validates <paramref name="request"/>'s <c>path</c> against the server's configured allowed
    /// input roots (spec §42), creates a new indexation run, and hands it off to the background
    /// worker for execution - this endpoint returns as soon as the run is queued, before any
    /// document or relation has actually been imported. Poll
    /// <c>GET /api/indexations/{indexationId}</c> with the id returned here to observe progress and
    /// the final outcome. Submitting the same file again is safe and idempotent: unchanged
    /// documents/relations are left untouched, and any document or relation no longer present in
    /// the file is removed only once the new run completes successfully.
    /// </remarks>
    /// <param name="request">The request body naming the CIIR JSONL file to import.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>202 Accepted</c> with an <see cref="IndexationAcceptedResponse"/> when the run was
    /// created and queued; <c>400</c>/<c>403</c>/<c>404</c> Problem Details when
    /// <paramref name="request"/>'s path fails validation (missing, outside the allowed roots, or
    /// does not exist/have the expected extension).
    /// </returns>
    [HttpPost]
    [ProducesResponseType<IndexationAcceptedResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartAsync([FromBody] StartIndexationRequest request, CancellationToken cancellationToken)
    {
        var result = await _startIndexation.ExecuteAsync(request.Path, cancellationToken);

        return result.Map(
            onSuccess: run => (IActionResult)Accepted(new IndexationAcceptedResponse(run.Id, run.Status.ToWireString())),
            onFailure: failure => failure.ToActionResult(HttpContext));
    }

    /// <summary>Reports the current status and progress of one indexation.</summary>
    /// <remarks>
    /// Intended to be polled after <c>POST /api/indexations</c> until <c>status</c> reaches a
    /// terminal value (<c>completed</c>, <c>failed</c>, or <c>cancelled</c>). While the run is still
    /// in progress (<c>pending</c>, <c>running</c>, or <c>resolving_relations</c>), the
    /// <c>documents</c>/<c>relations</c> counters reflect whatever partial progress has been
    /// persisted so far and may still change on the next poll.
    /// </remarks>
    /// <param name="id">The indexation id returned by <c>POST /api/indexations</c>.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>
    /// <c>200 OK</c> with an <see cref="IndexationStatusResponse"/> when <paramref name="id"/> is a
    /// known run; a body-less <c>404</c> otherwise.
    /// </returns>
    [HttpGet("{id}")]
    [ProducesResponseType<IndexationStatusResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await _runStore.GetAsync(id, cancellationToken);

        return run is null ? NotFound() : Ok(ToResponse(run));
    }

    private static IndexationStatusResponse ToResponse(IndexingRun run) => new(
        run.Id,
        run.Status.ToWireString(),
        new IndexationDocumentsSummary(
            run.Counters.DocumentsProcessed,
            run.Counters.DocumentsInserted,
            run.Counters.DocumentsUpdated,
            run.Counters.EmbeddingsGenerated,
            run.Counters.EmbeddingsReused),
        new IndexationRelationsSummary(
            run.Counters.RelationsProcessed,
            run.Counters.RelationsResolved,
            run.Counters.RelationsUnresolved),
        run.Error);
}
#pragma warning restore S6960
