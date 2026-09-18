namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Streams data in and out of an object storage bucket (upload spec §5). Every method is
/// streaming-first by contract: implementations must never buffer an entire object in memory, since
/// a single CIIR upload can be up to 200 MB (upload spec §12).
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Ensures <paramref name="bucket"/> exists, creating it if necessary. Called once at startup
    /// (upload spec §11) so a misconfigured bucket/credentials fails fast rather than on the first
    /// upload request.
    /// </summary>
    /// <param name="bucket">The bucket to ensure exists.</param>
    /// <param name="cancellationToken">Propagates startup cancellation.</param>
    Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>Streams <paramref name="content"/> into <paramref name="bucket"/> under <paramref name="objectKey"/>.</summary>
    /// <param name="bucket">The target bucket.</param>
    /// <param name="objectKey">The object key to write to.</param>
    /// <param name="content">The content stream, read forward-only exactly once.</param>
    /// <param name="contentLength">
    /// The content length in bytes, when known upfront; <c>null</c> when the caller is streaming a
    /// multipart section whose length is not known until fully read.
    /// </param>
    /// <param name="contentType">The MIME type to store alongside the object.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task UploadAsync(
        string bucket,
        string objectKey,
        Stream content,
        long? contentLength,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Streams an object from <paramref name="bucket"/> directly to a local file.</summary>
    /// <param name="bucket">The source bucket.</param>
    /// <param name="objectKey">The object key to read.</param>
    /// <param name="destinationPath">The local file path to write to; overwritten if it already exists.</param>
    /// <param name="cancellationToken">Propagates run cancellation.</param>
    Task DownloadToFileAsync(
        string bucket, string objectKey, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>Deletes an object. A no-op if it does not exist.</summary>
    /// <param name="bucket">The bucket to delete from.</param>
    /// <param name="objectKey">The object key to delete.</param>
    /// <param name="cancellationToken">Propagates request/run cancellation.</param>
    Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether an object already exists at <paramref name="objectKey"/> - used to validate
    /// a bring-your-own-upload registration (<c>POST /api/ciir-uploads/register</c>) before queuing
    /// it for processing, since nothing else in that flow ever writes the object itself.
    /// </summary>
    /// <param name="bucket">The bucket to check.</param>
    /// <param name="objectKey">The object key to check.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
}
