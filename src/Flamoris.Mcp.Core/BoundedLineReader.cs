using System.Text;

namespace Flamoris.Mcp.Core;

public enum McpFrameStatus { Success, EndOfStream, Oversized, InvalidUtf8, Truncated }

public sealed record McpFrameReadResult(McpFrameStatus Status, string? Line = null);

// MCP stdio uses newline-delimited UTF-8. This reader preserves bytes after a newline
// and bounds each frame before decoding it.
public sealed class BoundedLineReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Stream stream;
    private readonly int maximumBytes;
    private readonly byte[] buffer;
    private int offset;
    private int count;

    public BoundedLineReader(Stream stream, int maximumBytes = 4 * 1024 * 1024, int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (bufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(bufferSize));
        this.stream = stream;
        this.maximumBytes = maximumBytes;
        buffer = new byte[bufferSize];
    }

    public ValueTask<McpFrameReadResult> ReadAsync(CancellationToken token = default) =>
        ReadCoreAsync(frameTimeout: null, token);

    // Idle time before the first byte is intentionally unbounded. Once a frame has
    // started, the single deadline covers the rest of that frame and is not reset by
    // a client that trickles bytes.
    internal ValueTask<McpFrameReadResult> ReadFrameAsync(TimeSpan frameTimeout,
        CancellationToken token = default)
    {
        if (frameTimeout <= TimeSpan.Zero && frameTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(frameTimeout));
        return ReadCoreAsync(frameTimeout, token);
    }

    private async ValueTask<McpFrameReadResult> ReadCoreAsync(TimeSpan? frameTimeout,
        CancellationToken token)
    {
        using var frame = new MemoryStream(Math.Min(maximumBytes, buffer.Length));
        CancellationTokenSource? deadline = null;
        try
        {
            while (true)
            {
                if (count == 0)
                {
                    offset = 0;
                    count = await stream.ReadAsync(buffer.AsMemory(), deadline?.Token ?? token);
                    if (count == 0)
                        return frame.Length == 0
                            ? new(McpFrameStatus.EndOfStream)
                            : new(McpFrameStatus.Truncated);
                }

                int newline = Array.IndexOf(buffer, (byte)'\n', offset, count);
                int length = newline >= 0 ? newline - offset : count;
                if (frame.Length + length > maximumBytes)
                    return new(McpFrameStatus.Oversized);
                frame.Write(buffer, offset, length);
                offset += length;
                count -= length;

                if (newline >= 0)
                {
                    offset++;
                    count--;
                    var bytes = frame.GetBuffer().AsSpan(0, checked((int)frame.Length));
                    if (!bytes.IsEmpty && bytes[^1] == '\r') bytes = bytes[..^1];
                    try { return new(McpFrameStatus.Success, StrictUtf8.GetString(bytes)); }
                    catch (DecoderFallbackException) { return new(McpFrameStatus.InvalidUtf8); }
                }

                if (deadline is null && frameTimeout is { } timeout)
                {
                    deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(timeout);
                }
            }
        }
        finally { deadline?.Dispose(); }
    }
}

