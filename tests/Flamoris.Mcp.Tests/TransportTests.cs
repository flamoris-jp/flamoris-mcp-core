using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class TransportTests
{
    private static StdioClientTransport Transport(McpBoundary core, string secret)
    {
        string? executable = Environment.GetEnvironmentVariable("FLAMORIS_TEST_BRIDGE");
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Set FLAMORIS_TEST_BRIDGE to the published bridge executable.");
        return new(new() {
            Command = executable, Arguments = ["--pipe", core.Options.PipeName],
            EnvironmentVariables = new Dictionary<string, string?> { [StdioBridge.CredentialEnvironmentVariable] = secret },
            InheritEnvironmentVariables = true, ShutdownTimeout = TimeSpan.FromSeconds(5),
        });
    }
    [DataTestMethod]
    [DataRow("2026-07-28")]
    [DataRow("2025-03-26")]
    public async Task PublishedBridgeOfficialClientAndReconnectShareAuthority(string protocol)
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.Edit);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var serving = new LocalMcpEndpoint(core).RunAsync(grant, lifetime.Token);
        try
        {
            for (int connection = 0; connection < 2; connection++)
            {
                await using var client = await McpClient.CreateAsync(Transport(core, grant.ExportCredential()),
                    new() { ProtocolVersion = protocol }, cancellationToken: lifetime.Token);
                Assert.IsTrue(core.Status.Current.IsGreen);
                Assert.IsTrue(core.Status.Current.Connected);
                var tools = await client.ListToolsAsync(cancellationToken: lifetime.Token);
                Assert.IsTrue(tools.Any(t => t.Name == "value.set"));
                var context = await client.CallToolAsync("mcp.context", cancellationToken: lifetime.Token);
                Assert.IsFalse(context.IsError == true);
                var snapshot = context.StructuredContent!.Value;
                Assert.AreEqual(host.Snapshot.RuntimeId, snapshot.GetProperty("runtimeId").GetString());
                var result = await client.CallToolAsync("value.set", new Dictionary<string, object?> {
                    ["input"] = new { value = 10 + connection },
                    ["guard"] = new { runtimeId = host.Snapshot.RuntimeId, documentToken = host.Snapshot.DocumentToken,
                        expectedRevision = host.Snapshot.Revision.ToString() },
                }, cancellationToken: lifetime.Token);
                Assert.IsFalse(result.IsError == true, result.StructuredContent?.GetRawText());
                Assert.AreEqual(10 + connection, host.Value);
                host.Undo();
                var query = await client.CallToolAsync("value.read", new Dictionary<string, object?> { ["input"] = new {} },
                    cancellationToken: lifetime.Token);
                Assert.AreEqual(0, query.StructuredContent!.Value.GetProperty("value").GetInt32());
                host.Redo(); host.Undo();
            }
        }
        finally { lifetime.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.AreEqual(2, host.Commits); Assert.AreEqual(0, core.Status.Current.ForegroundCount);
        Assert.IsFalse(core.Status.Current.IsGreen);
    }

    [TestMethod]
    public async Task WrongCredentialAndMalformedConnectionDoNotPoisonHost()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.Edit);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = new LocalMcpEndpoint(core).RunAsync(grant, lifetime.Token);
        try {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await using var pipe = new NamedPipeClientStream(".", core.Options.PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(5000, lifetime.Token);
                await PipeHandshake.WriteAsync(pipe, new { version = 1, capability = new string('0', 64) }, lifetime.Token);
                string reply = await PipeHandshake.ReadAsync(pipe, lifetime.Token);
                StringAssert.Contains(reply, McpErrors.Unauthorized);
            }
            await using var client = await McpClient.CreateAsync(Transport(core, grant.ExportCredential()),
                cancellationToken: lifetime.Token);
            Assert.IsFalse((await client.CallToolAsync("mcp.context", cancellationToken: lifetime.Token)).IsError == true);
        }
        finally { lifetime.Cancel(); await serving.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.AreEqual(0, host.Commits);
    }

    [TestMethod]
    public async Task ReadOnlyCannotInvokeHiddenMutationAndStopRevokesConnection()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.ReadOnly);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = new LocalMcpEndpoint(core).RunAsync(grant, lifetime.Token);
        await using var client = await McpClient.CreateAsync(Transport(core, grant.ExportCredential()), cancellationToken: lifetime.Token);
        var tools = await client.ListToolsAsync(cancellationToken: lifetime.Token);
        Assert.IsFalse(tools.Any(t => t.Name == "value.set"));
        var denied = await client.CallToolAsync("value.set", new Dictionary<string, object?> {
            ["input"] = new { value = 3 }, ["guard"] = new { runtimeId = host.Snapshot.RuntimeId,
                documentToken = host.Snapshot.DocumentToken, expectedRevision = "0" },
        }, cancellationToken: lifetime.Token);
        Assert.IsTrue(denied.IsError == true);
        StringAssert.Contains(denied.StructuredContent!.Value.GetRawText(), McpErrors.Forbidden);
        core.Disable();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, host.Commits);
        Assert.IsFalse(core.Status.Current.IsGreen);
    }

    [TestMethod]
    public async Task MissingCredentialAndMissingHostAreMachineReadable()
    {
        var fault = await Assert.ThrowsExceptionAsync<McpFault>(() => StdioBridge.RunAsync(
            "flamoris-missing-" + Guid.NewGuid().ToString("N"), null, Stream.Null, Stream.Null));
        Assert.AreEqual(McpErrors.Unauthorized, fault.Code);
        fault = await Assert.ThrowsExceptionAsync<McpFault>(() => StdioBridge.RunAsync(
            "flamoris-missing-" + Guid.NewGuid().ToString("N"), new string('0', 64), Stream.Null, Stream.Null));
        Assert.AreEqual(McpErrors.HostUnavailable, fault.Code);
    }

    [DataTestMethod]
    [DataRow("ok\nnext\n", 4, McpFrameStatus.Success)]
    [DataRow("12345\n", 4, McpFrameStatus.Oversized)]
    [DataRow("partial", 10, McpFrameStatus.Truncated)]
    public async Task FramingBoundsBeforeDecoding(string input, int limit, McpFrameStatus status)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var reader = new BoundedLineReader(stream, limit, bufferSize: 2);
        Assert.AreEqual(status, (await reader.ReadAsync()).Status);
        if (status == McpFrameStatus.Success) Assert.AreEqual("next", (await reader.ReadAsync()).Line);
    }

    [TestMethod]
    public async Task InvalidUtf8AndDuplicateJsonFailClosed()
    {
        using var bytes = new MemoryStream(new byte[] { 0xff, 10 });
        Assert.AreEqual(McpFrameStatus.InvalidUtf8, (await new BoundedLineReader(bytes).ReadAsync()).Status);
        using var duplicate = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"method":"ping","params":{"a":1,"a":2}}""");
        Assert.ThrowsException<IOException>(() => BoundedProtocolStream.ValidateEnvelope(duplicate.RootElement));
        using var array = JsonDocument.Parse("[]");
        Assert.ThrowsException<IOException>(() => BoundedProtocolStream.ValidateEnvelope(array.RootElement));
    }

    [TestMethod]
    public void NamedPipeAddressCannotEscapeLocalNamespace()
    {
        foreach (string name in new[] { @"\\remote\pipe\test", "../flamoris-x", "flamoris-/x", "" })
            Assert.IsFalse(McpOptions.ValidPipeName(name));
        Assert.IsTrue(McpOptions.ValidPipeName("flamoris-" + Guid.NewGuid().ToString("N")));
        if (OperatingSystem.IsWindows()) Assert.AreEqual(8u, WindowsLocalPipe.RejectRemoteClients);
    }
}
