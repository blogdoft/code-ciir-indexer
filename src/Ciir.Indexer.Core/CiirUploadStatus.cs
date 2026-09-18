namespace Ciir.Indexer.Core;

/// <summary>The lifecycle states of one CIIR upload (upload spec §7).</summary>
public enum CiirUploadStatus
{
    /// <summary>The file was stored in MinIO and is waiting to be picked up by the worker.</summary>
    Pending,

    /// <summary>The worker has claimed this upload and is running the indexation for it.</summary>
    Processing,

    /// <summary>The indexation completed successfully; the MinIO object has been deleted.</summary>
    Processed,

    /// <summary>
    /// The upload will not be processed again - either the indexation itself failed, the
    /// referenced project no longer exists, or the worker exhausted its retry attempts. The MinIO
    /// object has been deleted.
    /// </summary>
    Failed,
}
