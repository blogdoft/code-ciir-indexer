namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Hands a newly created run off for background execution (spec §2's "kicks off... async"). The
/// concrete mechanism (a bounded channel consumed by a single background worker, per spec §33's
/// backpressure guidance) is an infrastructure/composition-root concern - this port only expresses
/// the intent, keeping <c>StartIndexation</c> free of any delivery-mechanism detail.
/// </summary>
public interface IIndexationQueue
{
    Task EnqueueAsync(Guid runId, CancellationToken cancellationToken = default);
}
