using Ciir.Indexer.Application.Ports;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ciir.Indexer.Infrastructure.ObjectStorage.S3.Tests;

/// <summary>
/// Exercises the real AWS S3 SDK against a Testcontainers-managed Garage instance (upload spec §13)
/// rather than mocking the SDK - the same "no fakes for the adapter's own tests" principle already
/// applied to PostgreSQL in this repository.
/// </summary>
[Trait("Category", "Integration")]
public sealed class S3ObjectStorageTests : IAsyncLifetime
{
    private const string Bucket = "ciir-uploads-tests";
    private const int S3Port = 3900;

    // Garage's default image is scratch-based (just the /garage binary), so all setup goes through
    // ExecAsync on that binary rather than a shell. Throwaway single-node config and credentials.
    private const string GarageConfig = """
        metadata_dir = "/var/lib/garage/meta"
        data_dir = "/var/lib/garage/data"
        db_engine = "lmdb"
        replication_factor = 1
        rpc_bind_addr = "[::]:3901"
        rpc_public_addr = "127.0.0.1:3901"
        rpc_secret = "1799bccfd7411eddcf9ebd316bc1f5287ad12a68094e1c6ac6abde7e6feae1ec"

        [s3_api]
        s3_region = "garage"
        api_bind_addr = "[::]:3900"
        """;

    private const string AccessKey = "GK000000000000000000000000";
    private const string SecretKey = "0000000000000000000000000000000000000000000000000000000000000000";

    private IContainer _container = null!;
    private IObjectStorage _sut = null!;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder("dxflrs/garage:v2.4.1")
            .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(GarageConfig), "/etc/garage.toml")
            .WithPortBinding(S3Port, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilExternalTcpPortIsAvailable(S3Port))
            .Build();
        await _container.StartAsync();

        var nodeId = (await Garage("node", "id", "-q")).Split('@')[0].Trim();
        await Garage("layout", "assign", "-z", "dc1", "-c", "1G", nodeId);
        await Garage("layout", "apply", "--version", "1");
        await Garage("key", "import", "--yes", "-n", "tests", AccessKey, SecretKey);
        await Garage("bucket", "create", Bucket);
        await Garage("bucket", "allow", "--read", "--write", "--owner", Bucket, "--key", AccessKey);
        await Garage("key", "allow", "--create-bucket", AccessKey);

        var services = new ServiceCollection();
        services.AddS3ObjectStorage(new S3Options
        {
            Endpoint = $"{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}",
            Region = "garage",
            AccessKey = AccessKey,
            SecretKey = SecretKey,
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
    public async Task EnsureBucketExistsAsync_MissingBucket_CreatesIt()
    {
        var bucket = $"missing-{Guid.NewGuid():N}";

        await _sut.EnsureBucketExistsAsync(bucket);

        using var uploadStream = new MemoryStream("data"u8.ToArray());
        await Should.NotThrowAsync(() => _sut.UploadAsync(bucket, "ciir.jsonl", uploadStream, 4, "application/x-ndjson"));
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

    private async Task<string> Garage(params string[] args)
    {
        var result = await _container.ExecAsync(["/garage", .. args]);
        result.ExitCode.ShouldBe(0, $"garage {string.Join(' ', args)} failed: {result.Stderr}");
        return result.Stdout;
    }
}
