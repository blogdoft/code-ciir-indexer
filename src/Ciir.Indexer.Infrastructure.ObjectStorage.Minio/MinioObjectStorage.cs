using Ciir.Indexer.Application.Ports;
using global::Minio;
using global::Minio.DataModel.Args;
using global::Minio.Exceptions;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.Minio;

/// <inheritdoc cref="IObjectStorage" />
public sealed class MinioObjectStorage : IObjectStorage
{
    // Passed to PutObjectArgs.WithObjectSize when the caller does not know the content length
    // upfront (a multipart/form-data file section's size is not known until fully read) - the
    // MinIO SDK treats this as "stream via multipart upload", reading and uploading bounded-size
    // chunks rather than buffering the whole object (upload spec §5/§12).
    private const long UnknownObjectSize = -1;

    private readonly IMinioClient _client;

    public MinioObjectStorage(IMinioClient client)
    {
        _client = client;
    }

    public Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var exists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucket), cancellationToken);
            if (!exists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken);
            }
        });

    public Task UploadAsync(
        string bucket,
        string objectKey,
        Stream content,
        long? contentLength,
        string contentType,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(objectKey)
                .WithStreamData(content)
                .WithObjectSize(contentLength ?? UnknownObjectSize)
                .WithContentType(contentType),
            cancellationToken));

    public Task DownloadToFileAsync(
        string bucket, string objectKey, string destinationPath, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _client.GetObjectAsync(
            new GetObjectArgs().WithBucket(bucket).WithObject(objectKey).WithFile(destinationPath),
            cancellationToken));

    public Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _client.RemoveObjectAsync(
            new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), cancellationToken));

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is MinioException or HttpRequestException)
        {
            throw new ObjectStorageUnavailableException($"MinIO operation failed: {ex.Message}", ex);
        }
    }
}
