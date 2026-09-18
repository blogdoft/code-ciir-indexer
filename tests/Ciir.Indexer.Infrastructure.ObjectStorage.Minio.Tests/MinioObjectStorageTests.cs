using Ciir.Indexer.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.Minio;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.Minio.Tests;

/// <summary>
/// Exercises the real MinIO SDK against a Testcontainers-managed MinIO instance (upload spec §13)
/// rather than mocking the SDK - the same "no fakes for the adapter's own tests" principle already
/// applied to PostgreSQL in this repository.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MinioObjectStorageTests : IAsyncLifetime
{
    private const string Bucket = "ciir-uploads-tests";

    private MinioContainer _container = null!;
    private IObjectStorage _sut = null!;

    public async Task InitializeAsync()
    {
        // Docker Hub's "minio/minio" now rejects anonymous pulls (MinIO publishes the open-source
        // AGPL image on Quay.io instead) - verified by hand in this environment.
        _container = new MinioBuilder("quay.io/minio/minio:latest").Build();
        await _container.StartAsync();

        var endpoint = _container.GetConnectionString()
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        services.AddMinioObjectStorage(new MinioOptions
        {
            Endpoint = endpoint,
            AccessKey = _container.GetAccessKey(),
            SecretKey = _container.GetSecretKey(),
            UseSsl = false,
            BucketName = Bucket,
        });
        _sut = services.BuildServiceProvider().GetRequiredService<IObjectStorage>();

        await _sut.EnsureBucketExistsAsync(Bucket);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task UploadAsync_ThenDownloadToFileAsync_RoundTripsTheContent()
    {
        var objectKey = $"{Guid.NewGuid():N}/ciir.jsonl";
        var content = "{\"schemaVersion\":\"1.0\"}"u8.ToArray();
        var destinationPath = Path.GetTempFileName();

        try
        {
            using (var uploadStream = new MemoryStream(content))
            {
                await _sut.UploadAsync(Bucket, objectKey, uploadStream, content.Length, "application/x-ndjson");
            }

            await _sut.DownloadToFileAsync(Bucket, objectKey, destinationPath);

            var downloaded = await File.ReadAllBytesAsync(destinationPath);
            downloaded.ShouldBe(content);
        }
        finally
        {
            File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task UploadAsync_UnknownContentLength_StreamsSuccessfully()
    {
        var objectKey = $"{Guid.NewGuid():N}/ciir.jsonl";
        var content = "{\"schemaVersion\":\"1.0\"}"u8.ToArray();
        var destinationPath = Path.GetTempFileName();

        try
        {
            using (var uploadStream = new MemoryStream(content))
            {
                await _sut.UploadAsync(Bucket, objectKey, uploadStream, contentLength: null, "application/x-ndjson");
            }

            await _sut.DownloadToFileAsync(Bucket, objectKey, destinationPath);

            var downloaded = await File.ReadAllBytesAsync(destinationPath);
            downloaded.ShouldBe(content);
        }
        finally
        {
            File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task DeleteAsync_ExistingObject_RemovesIt()
    {
        var objectKey = $"{Guid.NewGuid():N}/ciir.jsonl";
        using (var uploadStream = new MemoryStream("data"u8.ToArray()))
        {
            await _sut.UploadAsync(Bucket, objectKey, uploadStream, 4, "application/x-ndjson");
        }

        await _sut.DeleteAsync(Bucket, objectKey);

        var destinationPath = Path.GetTempFileName();
        try
        {
            await Should.ThrowAsync<ObjectStorageUnavailableException>(
                () => _sut.DownloadToFileAsync(Bucket, objectKey, destinationPath));
        }
        finally
        {
            File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task EnsureBucketExistsAsync_BucketAlreadyExists_DoesNotThrow()
    {
        await Should.NotThrowAsync(() => _sut.EnsureBucketExistsAsync(Bucket));
    }

    [Fact]
    public async Task ExistsAsync_ExistingObject_ReturnsTrue()
    {
        var objectKey = $"{Guid.NewGuid():N}/ciir.jsonl";
        using (var uploadStream = new MemoryStream("data"u8.ToArray()))
        {
            await _sut.UploadAsync(Bucket, objectKey, uploadStream, 4, "application/x-ndjson");
        }

        var exists = await _sut.ExistsAsync(Bucket, objectKey);

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsAsync_UnknownObject_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync(Bucket, $"{Guid.NewGuid():N}/ciir.jsonl");

        exists.ShouldBeFalse();
    }
}
