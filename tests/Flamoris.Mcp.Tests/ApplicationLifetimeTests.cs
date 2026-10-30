using System.Text.Json;
using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class ApplicationLifetimeTests
{
    [TestMethod]
    public async Task EmptyStartupReplacementAndCloseKeepTheSameCredential()
    {
        var host = new HostHarness(); host.Document(false);
        var app = new HostTool<JsonElement>("app.state", "Application state", HostHarness.Json(new { type = "object" }),
            OperationKind.Query, x => x, (ctx, _, _) => ctx.ReadAsync(() => HostHarness.Json(new { running = true }))) { RequiresDocument = false };
        using var boundary = host.Boundary(new() { ApplicationLifetime = true }, [app]);
        using var grant = await boundary.EnableAsync(McpPermission.Edit);
        var credential = grant.ExportCredential();
        Assert.IsFalse((await boundary.ContextAsync(grant, default)).GetProperty("hasDocument").GetBoolean());
        Assert.IsFalse((await boundary.InvokeAsync(grant, "app.state", HostHarness.Empty, null)).IsError);
        Assert.AreEqual(McpErrors.NoDocument, (await boundary.InvokeAsync(grant, "value.read", HostHarness.Empty, null)).Error);
        host.Document(true); var old = host.Guard(); host.Replace(revoke: false);
        Assert.IsTrue(grant.Authenticate(credential));
        Assert.AreEqual(McpErrors.StaleDocument, (await boundary.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 3 }), old)).Error);
        Assert.IsFalse((await boundary.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 4 }), host.Guard())).IsError);
        host.Document(false);
        Assert.IsTrue(grant.Authenticate(credential));
        Assert.AreEqual(McpErrors.NoDocument, (await boundary.InvokeAsync(grant, "value.read", HostHarness.Empty, null)).Error);
        host.Restart(); Assert.IsFalse(grant.IsActive);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReplacementRejectsPendingReadAndMutationWithoutDisconnecting(bool mutation)
    {
        var host = new HostHarness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new HostTool<JsonElement>("pending", "Barrier", HostHarness.Json(new { type = "object" }),
            mutation ? OperationKind.Mutation : OperationKind.Query, x => x, async (ctx, _, _) => {
                entered.SetResult(); await release.Task;
                return mutation ? await ctx.CommitAsync(() => host.Set(99)) : HostHarness.Json(new { privateData = "old document" });
            });
        using var boundary = host.Boundary(new() { ApplicationLifetime = true }, [tool]);
        using var grant = await boundary.EnableAsync(McpPermission.Edit);
        var pending = boundary.InvokeAsync(grant, "pending", HostHarness.Empty, mutation ? host.Guard() : null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        host.Replace(revoke: false); release.SetResult();
        Assert.AreEqual(McpErrors.StaleDocument, (await pending).Error);
        Assert.IsTrue(grant.IsActive); Assert.AreEqual(0, host.Commits);
    }

    [TestMethod]
    public async Task SharingRevocationRejectsPreparedContentButKeepsTransportGrant()
    {
        var host = new HostHarness(); bool sharing = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new HostTool<JsonElement>("observation", "Consent-bound content", HostHarness.Json(new { type = "object" }),
            OperationKind.Query, x => x, async (ctx, _, _) => {
                await ctx.BindDisclosureAsync(() => { if (!sharing) throw new McpFault(McpErrors.Forbidden); });
                entered.SetResult(); await release.Task; return HostHarness.Json(new { pixels = "private" });
            }) { RequiresDocument = false };
        using var boundary = host.Boundary(new() { ApplicationLifetime = true }, [tool]);
        using var grant = await boundary.EnableAsync(McpPermission.ReadOnly);
        var pending = boundary.InvokeAsync(grant, "observation", HostHarness.Empty, null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.InvokeAsync(() => { sharing = false; return true; }, default); release.SetResult();
        var result = await pending;
        Assert.AreEqual(McpErrors.Forbidden, result.Error); Assert.IsNull(result.Value); Assert.IsTrue(grant.IsActive);
    }
}
