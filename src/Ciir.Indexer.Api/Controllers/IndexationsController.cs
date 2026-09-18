using Ciir.Indexer.Api.Contracts;
using Ciir.Indexer.Application.Ports;
using Ciir.Indexer.Core;
using Microsoft.AspNetCore.Mvc;

namespace Ciir.Indexer.Api.Controllers;

/// <summary>
/// Reports the progress of one indexation run. Contains no indexing logic of its own - a run is
/// always created implicitly by the CIIR upload flow (<c>POST /api/ciir-uploads</c> or
/// <c>POST /api/ciir-uploads/register</c>), never by this controller; this only reads back its
/// status.
/// </summary>
[ApiController]
[Route("api/indexer/indexations")]
public sealed class IndexationsController : ControllerBase
{
    private readonly IIndexingRunStore _runStore;

    /// <summary>Initializes a new instance of the <see cref="IndexationsController"/> class.</summary>
    /// <param name="runStore">Reads back a run's current status and counters for <see cref="GetAsync"/>.</param>
    public IndexationsController(IIndexingRunStore runStore)
    {
        _runStore = runStore;
    }

    /// <summary>Reports the current status and progress of one indexation.</summary>
    /// <remarks>
    /// Intended to be polled using the <c>indexationId</c> reported by <c>GET /api/ciir-uploads/{uploadId}</c>
    /// once that upload starts processing, until <c>status</c> reaches a terminal value
    /// (<c>completed</c>, <c>failed</c>, or <c>cancelled</c>). While the run is still in progress
    /// (<c>pending</c>, <c>running</c>, or <c>resolving_relations</c>), the
    /// <c>documents</c>/<c>relations</c> counters reflect whatever partial progress has been
    /// persisted so far and may still change on the next poll.
    /// </remarks>
    /// <param name="id">The indexation id reported by <c>GET /api/ciir-uploads/{uploadId}</c>.</param>
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
