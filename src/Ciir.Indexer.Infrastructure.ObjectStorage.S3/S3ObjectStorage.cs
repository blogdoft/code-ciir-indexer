using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Ciir.Indexer.Application.Ports;
using System.Net;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.S3;

/// <inheritdoc cref="IObjectStorage" />
public sealed class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _client;

    public S3ObjectStorage(IAmazonS3 client)
    {
        _client = client;
    }

    public Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            // A one-key listing rather than HeadBucket/ListBuckets: it only needs the read permission
            // the least-privilege upload key already has on this bucket.
            try
            {
                await _client.ListObjectsV2Async(
                    new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
            }
        });

    public Task UploadAsync(
        string bucket,
        string objectKey,
        Stream content,
        long? contentLength,
        string contentType,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            // TransferUtility streams the source as bounded-size multipart parts, so it also handles
            // a forward-only stream of unknown length (a multipart/form-data file section's size is
            // not known until fully read) without buffering the whole object (upload spec §5/§12).
            // contentLength is therefore not needed here.
            using var transferUtility = new TransferUtility(_client);
            await transferUtility.UploadAsync(
                new TransferUtilityUploadRequest
                {
                    BucketName = bucket,
                    Key = objectKey,
                    InputStream = content,
                    ContentType = contentType,
                    AutoCloseStream = false,
                },
                cancellationToken);
        });

    public Task DownloadToFileAsync(
        string bucket, string objectKey, string destinationPath, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            using var response = await _client.GetObjectAsync(bucket, objectKey, cancellationToken);
            await response.WriteResponseStreamToFileAsync(destinationPath, append: false, cancellationToken);
        });

    public Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _client.DeleteObjectAsync(bucket, objectKey, cancellationToken));

    public Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            try
            {
                await _client.GetObjectMetadataAsync(bucket, objectKey, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        });

    private static async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new ObjectStorageUnavailableException($"Object storage operation failed: {ex.Message}", ex);
        }
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new ObjectStorageUnavailableException($"Object storage operation failed: {ex.Message}", ex);
        }
    }
}
