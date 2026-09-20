using System.Text;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

internal static class PipeHandshake
{
    internal static async Task<string> ReadAsync(Stream stream, CancellationToken token)
    {
        // Read exactly the preamble; do not prefetch the first SDK frame.
        var buffer = new byte[1024];
        var single = new byte[1];
        int length = 0;
        while (length < buffer.Length)
        {
            if (await stream.ReadAsync(single, token) == 0) throw new IOException("transport_unavailable");
            if (single[0] == 10) return new UTF8Encoding(false, true).GetString(buffer, 0, length);
            buffer[length++] = single[0];
        }
        throw new McpFault(McpErrors.InvalidRequest);
    }
    internal static async Task WriteAsync(Stream stream, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 1024) throw new McpFault(McpErrors.InvalidRequest);
        await stream.WriteAsync(bytes, token);
        await stream.WriteAsync(new byte[] { 10 }, token);
        await stream.FlushAsync(token);
    }
}
