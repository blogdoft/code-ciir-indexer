namespace Ciir.Indexer.Api.Uploads;

/// <summary>
/// Wraps a forward-only read stream and throws <see cref="CiirUploadTooLargeException"/> as soon as
/// more bytes than <c>maxLength</c> have been read - lets the upload endpoint reject an oversized
/// file mid-stream (upload spec §11) instead of buffering it first to check its size.
/// </summary>
internal sealed class SizeLimitedStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxLength;
    private long _bytesRead;

    /// <summary>Initializes a new instance of the <see cref="SizeLimitedStream"/> class.</summary>
    /// <param name="inner">The stream to wrap, read forward-only exactly once.</param>
    /// <param name="maxLength">The maximum number of bytes allowed to be read before an exception is thrown.</param>
    public SizeLimitedStream(Stream inner, long maxLength)
    {
        _inner = inner;
        _maxLength = maxLength;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Track(read);
        return read;
    }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        Track(read);
        return read;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Track(read);
        return read;
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Track(int bytesJustRead)
    {
        _bytesRead += bytesJustRead;
        if (_bytesRead > _maxLength)
        {
            throw new CiirUploadTooLargeException();
        }
    }
}
