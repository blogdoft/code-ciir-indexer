using Ciir.Indexer.Application;
using Ciir.Indexer.Application.UseCases;

namespace Ciir.Indexer.Api.Uploads;

/// <summary>
/// Polls the durable <c>ciir_uploads</c> queue for the lifetime of the host (upload spec §8): each
/// cycle processes at most one upload via <see cref="ProcessNextCiirUpload"/>, retrying immediately
/// while there is a backlog and waiting <see cref="UploadOptions.PollingIntervalSeconds"/> once
/// there is nothing eligible. Deliberately independent of <see cref="Indexation.IndexationWorker"/>'s
/// in-memory channel - this queue is durable, since an upload's file already sits in MinIO
/// regardless of whether this process is running (upload spec §2).
/// </summary>
public sealed class CiirUploadWorker : BackgroundService
{
    private readonly ProcessNextCiirUpload _processNext;
    private readonly UploadOptions _options;
    private readonly ILogger<CiirUploadWorker> _logger;

    /// <summary>Initializes a new instance of the <see cref="CiirUploadWorker"/> class.</summary>
    /// <param name="processNext">Claims and processes at most one upload per call.</param>
    /// <param name="options">Configures the polling interval.</param>
    /// <param name="logger">Logs unexpected failures so a single bad upload never crashes the host.</param>
    public CiirUploadWorker(ProcessNextCiirUpload processNext, UploadOptions options, ILogger<CiirUploadWorker> logger)
    {
        _processNext = processNext;
        _options = options;
        _logger = logger;
    }

    /// <summary>Runs the poll/process cycle until <paramref name="stoppingToken"/> is triggered.</summary>
    /// <param name="stoppingToken">Signaled when the host begins a graceful shutdown.</param>
    /// <returns>A task that completes when <paramref name="stoppingToken"/> is triggered.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await TryProcessNextAsync(stoppingToken);

            if (!processed)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task<bool> TryProcessNextAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await _processNext.ExecuteAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            // A single upload's infrastructure failure (MinIO/database blip) must never crash the
            // whole host - the row stays "processing" and the stuck-timeout mechanism (upload spec
            // §8) will reclaim it on a later cycle, bounded by MaxRetryCount.
            _logger.LogError(ex, "Unexpected failure while processing a CIIR upload.");
            return false;
        }
    }
}
