using Flamoris.Logging;

namespace Flamoris.Mcp.Core;

// Only curated metadata enters logging. Never forward exception messages or payloads.
public sealed class McpDiagnostics(FlamorisLogger logger)
{
    internal void Event(string category, string outcome, string? tool = null, long? durationMs = null)
    {
        try
        {
            logger.Info(category, "MCP boundary event", new Dictionary<string, object?>
            {
                ["transport"] = "stdioBridge", ["outcome"] = outcome,
                ["tool"] = tool, ["durationMs"] = durationMs,
            });
        }
        catch { /* logging is never editing authority */ }
    }
}
