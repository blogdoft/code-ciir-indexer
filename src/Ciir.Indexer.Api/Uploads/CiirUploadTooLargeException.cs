namespace Ciir.Indexer.Api.Uploads;

/// <summary>
/// The <c>ciirFile</c> multipart section exceeded the configured maximum size (upload spec §11),
/// detected mid-stream by <see cref="SizeLimitedStream"/> rather than after buffering the whole
/// file.
/// </summary>
public sealed class CiirUploadTooLargeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CiirUploadTooLargeException"/> class.</summary>
    public CiirUploadTooLargeException()
        : base("The uploaded file exceeds the configured maximum size.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CiirUploadTooLargeException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    public CiirUploadTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CiirUploadTooLargeException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="inner">The exception that caused this one.</param>
    public CiirUploadTooLargeException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
