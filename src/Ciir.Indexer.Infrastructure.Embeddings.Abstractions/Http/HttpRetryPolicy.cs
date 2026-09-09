using Microsoft.Extensions.Logging;
using System.Net;

namespace Ciir.Indexer.Infrastructure.Embeddings.Abstractions.Http;

/// <summary>
/// Exponential backoff with jitter for transient HTTP failures (429/502/503/504, timeouts,
/// connection errors), shared by every HTTP-based embedding provider module so the retry/backoff
/// policy is defined exactly once.
/// </summary>
public sealed class HttpRetryPolicy
{
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    private readonly string _providerName;
    private readonly int _maxRetries;
    private readonly ILogger _logger;

    public HttpRetryPolicy(string providerName, int maxRetries, ILogger logger)
    {
        _providerName = providerName;
        _maxRetries = maxRetries;
        _logger = logger;
    }

    /// <summary>Whether an HTTP response with this status code should be retried.</summary>
    /// <param name="statusCode">The status code to check.</param>
    public static bool IsTransientStatusCode(HttpStatusCode statusCode) => TransientStatusCodes.Contains(statusCode);

    /// <summary>
    /// Decides whether attempt number <paramref name="attempt"/> (0-based) that just failed for
    /// <paramref name="reason"/> should be retried, sleeping for the computed backoff if so.
    /// Returns false once <c>MaxRetries</c> has been exhausted.
    /// </summary>
    /// <param name="attempt">The 0-based attempt number that just failed.</param>
    /// <param name="reason">A short description of the failure, for logging.</param>
    /// <param name="cancellationToken">Propagates run cancellation during the backoff delay.</param>
    public async Task<bool> ShouldRetryAsync(int attempt, string reason, CancellationToken cancellationToken)
    {
        if (attempt >= _maxRetries)
        {
            return false;
        }

        var delay = ComputeBackoff(attempt);
        _logger.LogWarning(
            "Embedding request to provider '{Provider}' failed ({Reason}); retrying in {Delay} (attempt {Attempt}/{MaxRetries}).",
            _providerName,
            reason,
            delay,
            attempt + 1,
            _maxRetries);

        await Task.Delay(delay, cancellationToken);
        return true;
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var baseDelayMs = 500d * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.Next(0, 250);
        var totalMs = Math.Min(baseDelayMs + jitterMs, 30_000);
        return TimeSpan.FromMilliseconds(totalMs);
    }
}
