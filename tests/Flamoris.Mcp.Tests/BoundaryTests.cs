using System.Text.Json;
using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class BoundaryTests
{
    [TestMethod]
    public async Task CapabilityRotationAndReplacementAreIrreversible()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var a = await core.EnableAsync(McpPermission.Edit);
        string secret = a.ExportCredential();
        Assert.IsFalse(a.Authenticate(null)); Assert.IsFalse(a.Authenticate(new string('0', 64)));
        Assert.IsTrue(a.Authenticate(secret)); Assert.IsFalse(a.ToString().Contains(secret));
        using var b = await core.EnableAsync(McpPermission.ReadOnly);
        Assert.IsFalse(a.Authenticate(secret)); Assert.AreNotEqual(secret, b.ExportCredential());
        host.Replace(); Assert.IsFalse(b.IsActive);
        using var c = await core.EnableAsync(McpPermission.Edit);
        host.Restart(); Assert.IsFalse(c.IsActive);
        using var d = await core.EnableAsync(McpPermission.Edit);
        core.Disable(); Assert.IsFalse(d.IsActive);
    }

    [DataTestMethod]
    [DataRow("flamoris.kachinco")]
    [DataRow("flamoris.cutwork")]
    [DataRow("flamoris.2d")]
    public async Task SharedAuthorityHistoryAndRevision(string productId)
    {
        var host = new HostHarness(productId); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.Edit);
        var guard = host.Guard();
        Assert.IsFalse((await core.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 7 }), guard)).IsError);
        Assert.AreEqual(7, host.Value);
        Assert.AreEqual(McpErrors.StaleRevision, (await core.InvokeAsync(grant, "value.set",
            HostHarness.Json(new { value = 9 }), guard)).Error);
        host.Undo(); Assert.AreEqual(0, host.Value);
        Assert.IsFalse((await core.InvokeAsync(grant, "history.redo", HostHarness.Empty, host.Guard())).IsError);
        host.Set(11); // human gesture through the same authority
        var read = await core.InvokeAsync(grant, "value.read", HostHarness.Empty, null);
        Assert.AreEqual(11, read.Value!.Value.GetProperty("value").GetInt32());
        Assert.IsFalse((await core.InvokeAsync(grant, "history.undo", HostHarness.Empty, host.Guard())).IsError);
        Assert.AreEqual(7, host.Value);
    }

    [TestMethod]
    public async Task PermissionsAndUnknownCapabilitiesFailClosed()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var readOnly = await core.EnableAsync(McpPermission.ReadOnly);
        Assert.AreEqual(McpErrors.Forbidden, (await core.InvokeAsync(readOnly, "value.set",
            HostHarness.Json(new { value = 1 }), host.Guard())).Error);
        using var edit = await core.EnableAsync(McpPermission.Edit);
        foreach (string name in new[] { "file.read", "process.run", "network.fetch", "eval" })
            Assert.AreEqual(McpErrors.UnsupportedCapability, (await core.InvokeAsync(edit, name, HostHarness.Empty, null)).Error);
        Assert.AreEqual(0, host.Commits);
    }

    [TestMethod]
    public async Task StaleTokensAndBusyRejectWithoutMutation()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.Edit);
        var input = HostHarness.Json(new { value = 1 });
        Assert.AreEqual(McpErrors.StaleSession, (await core.InvokeAsync(grant, "value.set", input,
            host.Guard() with { RuntimeId = "old" })).Error);
        Assert.AreEqual(McpErrors.StaleDocument, (await core.InvokeAsync(grant, "value.set", input,
            host.Guard() with { DocumentToken = "old" })).Error);
        Assert.AreEqual(McpErrors.InvalidRequest, (await core.InvokeAsync(grant, "value.set", input, null)).Error);
        host.Busy(true);
        Assert.AreEqual(McpErrors.Busy, (await core.InvokeAsync(grant, "value.set", input, host.Guard())).Error);
        host.Busy(false);
        Assert.IsFalse((await core.InvokeAsync(grant, "value.set", input, host.Guard())).IsError);
    }

    [DataTestMethod]
    [DataRow("cancel")]
    [DataRow("timeout")]
    [DataRow("revoke")]
    [DataRow("replace")]
    public async Task PreparationCannotCommitAfterTerminalBoundary(string reason)
    {
        var host = new HostHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new HostTool<JsonElement>("slow", "Barrier-controlled preparation.", HostHarness.Json(new { type = "object" }),
            OperationKind.Mutation, x => x, async (ctx, _, _) => {
                entered.SetResult(); await resume.Task; // deliberately ignores cancellation during preparation
                try { return await ctx.CommitAsync(() => host.Set(99)); }
                finally { exited.SetResult(); }
            });
        using var core = host.Boundary(new() { RequestTimeoutMs = reason == "timeout" ? 100 : 5000, MaxConcurrentRequests = 1 }, [slow]);
        using var grant = await core.EnableAsync(McpPermission.Edit);
        using var cancel = new CancellationTokenSource();
        var call = core.InvokeAsync(grant, "slow", HostHarness.Empty, host.Guard(), cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(1, core.Status.Current.ForegroundCount);
        if (reason == "cancel") cancel.Cancel();
        if (reason == "revoke") core.Disable();
        if (reason == "replace") host.Replace();
        var result = await call.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.IsError);
        if (reason == "timeout") Assert.AreEqual(McpErrors.Timeout, result.Error);
        if (reason == "cancel") Assert.AreEqual(McpErrors.Cancelled, result.Error);
        Assert.AreEqual(0, core.Status.Current.ForegroundCount);
        if (grant.IsActive)
            Assert.AreEqual(McpErrors.Busy, (await core.InvokeAsync(grant, "value.read", HostHarness.Empty, null)).Error);
        resume.SetResult(); await exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, host.Commits);
    }

    [TestMethod]
    public async Task ConcurrentExpectedRevisionAllowsOnlyOneCommit()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.Edit); var guard = host.Guard();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            core.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = i }), guard)));
        Assert.AreEqual(1, results.Count(r => !r.IsError)); Assert.AreEqual(1, host.Commits);
    }

    [TestMethod]
    public async Task FailedRequestAndObserverDoNotPoisonNextRequest()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        core.Status.Changed += () => throw new InvalidOperationException("observer");
        using var grant = await core.EnableAsync(McpPermission.Edit);
        Assert.IsTrue((await core.InvokeAsync(grant, "history.undo", HostHarness.Empty, host.Guard())).IsError);
        Assert.IsFalse((await core.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 2 }), host.Guard())).IsError);
        Assert.AreEqual(0, core.Status.Current.ForegroundCount);
    }

    [TestMethod]
    public async Task ActivityScopesAreIdempotentAndBackgroundDoesNotFlash()
    {
        var host = new HostHarness(); using var core = host.Boundary();
        using var grant = await core.EnableAsync(McpPermission.ReadOnly);
        int maximum = 0;
        core.Status.Changed += () => maximum = Math.Max(maximum, core.Status.Current.ForegroundCount);
        await core.InvokeAsync(grant, "value.read", HostHarness.Empty, null);
        Assert.AreEqual(0, maximum);
        var a = core.Status.BeginForeground(); var b = core.Status.BeginForeground();
        Assert.AreEqual(2, core.Status.Current.ForegroundCount);
        a.Dispose(); a.Dispose(); Assert.AreEqual(1, core.Status.Current.ForegroundCount);
        core.Disable(); b.Dispose(); Assert.AreEqual(0, core.Status.Current.ForegroundCount);
        Assert.IsFalse(core.Status.Current.IsGreen);
    }
}
