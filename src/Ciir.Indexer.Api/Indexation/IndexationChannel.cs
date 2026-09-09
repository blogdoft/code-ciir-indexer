using Ciir.Indexer.Application.Ports;
using System.Threading.Channels;

namespace Ciir.Indexer.Api.Indexation;

/// <summary>
/// The bounded queue backing async indexation execution (spec §2/§33). A single writer side
/// (<see cref="IIndexationQueue"/>, used by <c>StartIndexation</c>) and a single reader side
/// (<see cref="Reader"/>, consumed by <see cref="IndexationWorker"/>) - registered as one singleton
/// so both ends share the same channel.
/// </summary>
public sealed class IndexationChannel : IIndexationQueue
{
    private readonly Channel<Guid> _channel;

    /// <summary>Initializes a new instance of the <see cref="IndexationChannel"/> class.</summary>
    /// <param name="capacity">
    /// Maximum number of queued-but-not-yet-started run ids. Once full, <see cref="EnqueueAsync"/>
    /// waits rather than dropping or rejecting - this is the bounded-backpressure mechanism spec
    /// §33 calls for.
    /// </param>
    public IndexationChannel(int capacity)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Gets the reader side, consumed exclusively by <see cref="IndexationWorker"/>.</summary>
    /// <value>A single-reader <see cref="ChannelReader{T}"/> of run ids.</value>
    public ChannelReader<Guid> Reader => _channel.Reader;

    /// <summary>Queues a run id for background execution, waiting if the channel is currently full.</summary>
    /// <param name="runId">The <c>indexing_runs.id</c> to execute.</param>
    /// <param name="cancellationToken">Propagates request cancellation while waiting for space.</param>
    /// <returns>A task that completes once the run id has been accepted onto the channel.</returns>
    public async Task EnqueueAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await _channel.Writer.WriteAsync(runId, cancellationToken);
}
