namespace Ciir.Indexer.Infrastructure.ObjectStorage.S3;

/// <summary>The object storage could not be reached, or an operation against it failed.</summary>
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
