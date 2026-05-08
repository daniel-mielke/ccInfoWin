namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// A Stream wrapper that intercepts byte-level '\n' boundaries and fires a callback
/// after each complete line has been returned to the reader.
/// Used in DROPDOWN-06 regression tests to deterministically inject bytes into a
/// JSONL file during an active read pass.
/// </summary>
internal sealed class ControllableStreamProxy : Stream
{
    private readonly Stream _inner;
    private int _lineCount;

    /// <summary>
    /// Invoked after each newline byte (0x0A) is returned to the caller.
    /// The int argument is the 1-based index of the line just completed.
    /// </summary>
    public Action<int>? OnAfterReadLine { get; set; }

    public ControllableStreamProxy(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        for (var i = offset; i < offset + bytesRead; i++)
        {
            if (buffer[i] == 0x0A) // newline '\n'
            {
                _lineCount++;
                OnAfterReadLine?.Invoke(_lineCount);
            }
        }
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void Flush() => _inner.Flush();

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("ControllableStreamProxy is read-only.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
