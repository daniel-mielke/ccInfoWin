namespace CCInfoWindows.Tests.Helpers;

/// <summary>
/// A read-only Stream wrapper with two injection points: after each complete line has been handed to
/// the reader, and at the moment the reader first hits end-of-stream.
///
/// Used by the DROPDOWN-06 regression test to append bytes to a JSONL file at the one instant that
/// makes <c>stream.Position</c> and <c>stream.Length</c> disagree. Appending after the read pass has
/// finished cannot reproduce it (both values then name the same offset), and appending while lines are
/// still being consumed cannot either — the reader simply picks the new bytes up in the same pass.
/// </summary>
internal sealed class ControllableStreamProxy : Stream
{
    private readonly Stream _inner;
    private int _lineCount;
    private bool _endOfStreamReported;

    /// <summary>
    /// Invoked after each newline byte (0x0A) is returned to the caller.
    /// The int argument is the 1-based index of the line just completed.
    /// </summary>
    public Action<int>? OnAfterReadLine { get; set; }

    /// <summary>
    /// Invoked once, inside the first <see cref="Read(byte[], int, int)"/> that returns zero bytes — the
    /// reader has reached the end of the content and is about to stop asking for more. Bytes appended
    /// from here are invisible to the current read pass but already counted by <see cref="Length"/>,
    /// which is exactly the state a resume position must survive.
    /// </summary>
    public Action? OnEndOfStream { get; set; }

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

        if (bytesRead == 0)
        {
            ReportEndOfStreamOnce();
            return bytesRead;
        }

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

    /// <summary>
    /// Once per instance: a callback that appends bytes would otherwise be re-entered by the very read
    /// it triggered, growing the file without bound.
    /// </summary>
    private void ReportEndOfStreamOnce()
    {
        if (_endOfStreamReported)
            return;

        _endOfStreamReported = true;
        OnEndOfStream?.Invoke();
    }
}
