using Flamoris.Logging;
using Flamoris.Mcp.Core;

// Capture the protocol stream before redirecting managed console logging to stderr.
using var output = Console.OpenStandardOutput();
Console.SetOut(Console.Error);
var diagnostics = new McpDiagnostics(FlamorisLogger.Create());
string? capability = Environment.GetEnvironmentVariable(StdioBridge.CredentialEnvironmentVariable);
Environment.SetEnvironmentVariable(StdioBridge.CredentialEnvironmentVariable, null);
if (args.Length != 2 || args[0] != "--pipe" || !McpOptions.ValidPipeName(args[1]))
{ Console.Error.WriteLine("{\"error\":\"invalid_request\"}"); return 2; }
using var lifetime = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
try
{
    await StdioBridge.RunAsync(args[1], capability, Console.OpenStandardInput(), output,
        cancellationToken: lifetime.Token);
    return 0;
}
catch (McpFault fault)
{ Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { error = fault.Code })); return 1; }
catch (OperationCanceledException)
{ Console.Error.WriteLine("{\"error\":\"cancelled\"}"); return 1; }
catch
{ Console.Error.WriteLine("{\"error\":\"transport_unavailable\"}"); return 1; }
