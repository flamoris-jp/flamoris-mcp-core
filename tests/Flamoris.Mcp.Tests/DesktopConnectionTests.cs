using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class DesktopConnectionTests
{
    [TestMethod]
    public void DefaultsAreManualReadonlyAndNoAutomaticConnection()
    {
        var settings = new DesktopConnectionSettings();
        Assert.IsFalse(settings.AutoConnect);
        Assert.AreEqual(McpPermission.ReadOnly, settings.Permission);
        Assert.AreEqual(DesktopConnectionMethod.Manual, settings.Method);
        Assert.IsFalse((settings with { AutoConnect = true }).RequiresReconnect(settings));
        Assert.IsTrue((settings with { Permission = McpPermission.Edit }).RequiresReconnect(settings));
        Assert.IsFalse(new McpStatus(true, true, false, 0, null).IsGreen);
        Assert.IsTrue(new McpStatus(true, true, true, 0, null).IsGreen);
    }
    [TestMethod]
    public async Task OnlySuccessfulMutationsProduceAcknowledgement()
    {
        var host = new HostHarness(); using var boundary = host.Boundary();
        using var grant = await boundary.EnableAsync(McpPermission.Edit);
        int pulses = 0; boundary.Status.MutationSucceeded += () => pulses++;
        await boundary.InvokeAsync(grant, "value.read", HostHarness.Json(new { }), null);
        Assert.AreEqual(0, pulses);
        Assert.IsFalse((await boundary.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 9 }), host.Guard())).IsError);
        Assert.AreEqual(1, pulses);
        await boundary.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 2 }), null);
        Assert.AreEqual(1, pulses);
        using var read = await boundary.EnableAsync(McpPermission.ReadOnly);
        await boundary.InvokeAsync(read, "value.set", HostHarness.Json(new { value = 3 }), host.Guard());
        Assert.AreEqual(1, pulses);
    }
    [TestMethod]
    public void HubRequiresTlsAndRejectsCredentialsInEndpoint()
    {
        foreach (string uri in new[] { "ws://host/desktop/app", "wss://user:secret@host/app", "wss://host/app?token=x", "wss://host/app#x" })
            Assert.ThrowsException<ArgumentException>(() => new HubConnectionSettings(new(uri)).Validate());
        new HubConnectionSettings(new("wss://host/desktop/app")).Validate();
    }
    [TestMethod]
    public async Task HubRegistersAndDispatchesThroughCurrentBoundaryThenRevokes()
    {
        var host = new HostHarness(); using var boundary = host.Boundary();
        using var grant = await boundary.EnableAsync(McpPermission.Edit);
        var socket = new FakeSocket();
        var provider = new HubConnectionProvider(new(new Uri("wss://hub.test/desktop/test")), boundary,
            grant, _ => ValueTask.FromResult(new string('x', 32))) { SocketFactory = () => socket };
        await provider.StartAsync(default);
        using var register = await socket.Next();
        Assert.AreEqual("register", register.RootElement.GetProperty("type").GetString());
        Assert.AreEqual(grant.Snapshot.RuntimeId, register.RootElement.GetProperty("runtimeId").GetString());
        Assert.IsFalse(register.RootElement.GetRawText().Contains(grant.ExportCredential()));
        Assert.IsFalse(boundary.Status.Current.IsGreen);
        await socket.Push(new { type = "ready", version = 1 });
        await socket.Push(new { type = "call", id = "1", name = "value.set", arguments = new {
            input = new { value = 5 }, guard = new { runtimeId = host.Snapshot.RuntimeId,
                documentToken = host.Snapshot.DocumentToken, expectedRevision = "0" } } });
        using var response = await socket.Next();
        Assert.AreEqual("result", response.RootElement.GetProperty("type").GetString());
        Assert.IsFalse(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.AreEqual(5, host.Value);
        Assert.IsTrue(boundary.Status.Current.IsGreen);
        boundary.Disable(); await provider.StopAsync(default);
        Assert.IsFalse(boundary.Status.Current.IsGreen);
        Assert.IsTrue(socket.Aborted);
        Assert.AreEqual(1, host.Commits);
    }
    [TestMethod]
    public async Task ReadonlyHubDoesNotAdvertiseOrAcceptMutation()
    {
        var host = new HostHarness(); using var boundary = host.Boundary();
        using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var socket = new FakeSocket();
        var provider = new HubConnectionProvider(new(new Uri("wss://hub.test/desktop/test")), boundary,
            grant, _ => ValueTask.FromResult(new string('x', 32))) { SocketFactory = () => socket };
        await provider.StartAsync(default);
        using var register = await socket.Next();
        Assert.IsFalse(register.RootElement.GetProperty("tools").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "value.set"));
        await socket.Push(new { type = "ready", version = 1 });
        await socket.Push(new { type = "call", id = "1", name = "value.set", arguments = new { input = new { value = 5 } } });
        using var response = await socket.Next();
        Assert.IsTrue(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.AreEqual(0, host.Commits);
        boundary.Disable(); await provider.StopAsync(default);
    }
    private sealed class FakeSocket : IHubSocket
    {
        private readonly Channel<byte[]> input = Channel.CreateUnbounded<byte[]>();
        private readonly Channel<byte[]> output = Channel.CreateUnbounded<byte[]>();
        public bool Aborted { get; private set; }
        public Task ConnectAsync(Uri uri, string secret, CancellationToken token) => Task.CompletedTask;
        public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => output.Writer.WriteAsync(bytes.ToArray(), token);
        public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> bytes, CancellationToken token)
        {
            byte[] data = await input.Reader.ReadAsync(token); data.CopyTo(bytes);
            return new(data.Length, WebSocketMessageType.Text, true);
        }
        public ValueTask Push(object value) => input.Writer.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(value));
        public async Task<JsonDocument> Next() => JsonDocument.Parse(await output.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        public void Abort() { Aborted = true; input.Writer.TryComplete(); }
        public void Dispose() => Abort();
    }
}
