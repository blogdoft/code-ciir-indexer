namespace Ciir.Indexer.Infrastructure.ObjectStorage.Minio;

/// <summary>Strongly typed binding of the "Minio" configuration section (upload spec §10).</summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>The MinIO server's host and port (e.g. "minio.internal:9000"), without a scheme.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Whether to connect to <see cref="Endpoint"/> over TLS.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>The bucket CIIR uploads are stored in (upload spec §5).</summary>
    public string BucketName { get; set; } = string.Empty;
}
