namespace Ciir.Indexer.Infrastructure.ObjectStorage.Minio;

/// <summary>MinIO could not be reached, or an object storage operation failed.</summary>
public sealed class ObjectStorageUnavailableException : Exception
{
    public ObjectStorageUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public ObjectStorageUnavailableException()
    {
    }

    public ObjectStorageUnavailableException(string message)
        : base(message)
    {
    }
}
