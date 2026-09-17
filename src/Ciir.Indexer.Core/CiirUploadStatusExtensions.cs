namespace Ciir.Indexer.Core;

/// <summary>
/// The canonical lowercase string vocabulary for <see cref="CiirUploadStatus"/> (upload spec §7),
/// shared by every layer that serializes it - the persisted <c>ciir_uploads.status</c> column and
/// the <c>GET /api/ciir-uploads/{id}</c> response both use these exact tokens.
/// </summary>
public static class CiirUploadStatusExtensions
{
    public static string ToWireString(this CiirUploadStatus status) => status switch
    {
        CiirUploadStatus.Pending => "pending",
        CiirUploadStatus.Processing => "processing",
        CiirUploadStatus.Processed => "processed",
        CiirUploadStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };

    public static CiirUploadStatus ParseCiirUploadStatus(string status) => status switch
    {
        "pending" => CiirUploadStatus.Pending,
        "processing" => CiirUploadStatus.Processing,
        "processed" => CiirUploadStatus.Processed,
        "failed" => CiirUploadStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, message: null),
    };
}
