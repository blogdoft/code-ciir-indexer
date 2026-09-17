namespace Ciir.Indexer.Application;

/// <summary>
/// Configuration for the CIIR upload endpoint and its polling worker (upload spec §10) -
/// deliberately separate from <see cref="IndexingOptions"/>, which governs the indexation engine
/// itself, not the upload/blob lifecycle.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    /// <summary>Maximum accepted size, in bytes, for an uploaded CIIR file (upload spec §4/§11).</summary>
    public long MaxCiirFileSizeBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>
    /// How long an upload may remain in <see cref="Core.CiirUploadStatus.Processing"/> before the
    /// worker considers it abandoned and eligible to be claimed again (upload spec §7/§8).
    /// </summary>
    public int StuckProcessingTimeoutMinutes { get; set; } = 30;

    /// <summary>How long the worker waits before polling again when nothing was eligible to process (upload spec §8).</summary>
    public int PollingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// How many times a stuck upload may be re-claimed before it is marked permanently failed
    /// instead (upload spec §7) - bounds retries to the "worker crashed mid-run" case, never a
    /// deterministic indexation failure.
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>Local directory the worker downloads an upload's file into before indexing it (upload spec §8).</summary>
    public string StagingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "ciir-uploads");

    /// <summary>Maximum number of uploads accepted concurrently by the endpoint (upload spec §11).</summary>
    public int MaxConcurrentUploads { get; set; } = 3;
}
