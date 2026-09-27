namespace Ciir.Indexer.Infrastructure.ObjectStorage.S3;

/// <summary>Strongly typed binding of the "ObjectStorage" configuration section (upload spec §10).</summary>
public sealed class S3Options
{
    public const string SectionName = "ObjectStorage";

    /// <summary>The S3-compatible server's host and port (e.g. "garage.internal:3900"), without a scheme.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The signing region. Must match the server's configured one - Garage's <c>s3_region</c>
    /// (default "garage"), not an AWS region name.
    /// </summary>
    public string Region { get; set; } = "garage";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Whether to connect to <see cref="Endpoint"/> over TLS.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>The bucket CIIR uploads are stored in (upload spec §5).</summary>
    public string BucketName { get; set; } = string.Empty;
}
