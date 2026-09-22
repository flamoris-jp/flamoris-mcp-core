using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class ManagedConnectionTests
{
    [TestMethod]
    public async Task StartStopAndExternalAttachmentRemainDistinct()
    {
        var events = new List<string>();
        var provider = new FakeProvider(events);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            events.Add("revoke");
            return ValueTask.CompletedTask;
        });

        await lifecycle.MarkEnabledAsync();
        Assert.IsTrue(lifecycle.Current.McpEnabled);
        Assert.AreEqual(ManagedConnectionProviderState.Stopped, lifecycle.Current.ProviderState);

        await lifecycle.StartAsync();
        Assert.AreEqual(ManagedConnectionProviderState.Running, lifecycle.Current.ProviderState);
        Assert.IsFalse(lifecycle.Current.ExternalClientConnected);
        lifecycle.SetExternalClientConnected(true);
        Assert.IsTrue(lifecycle.Current.ExternalClientConnected);

        await lifecycle.StopAsync();
        Assert.IsTrue(lifecycle.Current.McpEnabled, "Manual connection must remain possible after helper stop.");
        Assert.AreEqual(ManagedConnectionProviderState.Stopped, lifecycle.Current.ProviderState);
        Assert.IsFalse(lifecycle.Current.ExternalClientConnected);
        CollectionAssert.AreEqual(new[] { "start", "stop" }, events);
    }

    [TestMethod]
    public async Task DuplicateStartIsIdempotentAndRefreshUsesLatestProviderMaterial()
    {
        var events = new List<string>();
        var provider = new FakeProvider(events);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ => ValueTask.CompletedTask);
        await lifecycle.MarkEnabledAsync();

        await Task.WhenAll(lifecycle.StartAsync(), lifecycle.StartAsync());
        await lifecycle.RefreshAsync();

        Assert.AreEqual(1, provider.StartCount);
        Assert.AreEqual(1, provider.RefreshCount);
        CollectionAssert.AreEqual(new[] { "start", "refresh" }, events);
        Assert.AreEqual(ManagedConnectionProviderState.Running, lifecycle.Current.ProviderState);
    }

    [TestMethod]
    public async Task DisableRevokesBeforeStoppingOwnedProvider()
    {
        var events = new List<string>();
        var provider = new FakeProvider(events);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            events.Add("revoke");
            return ValueTask.CompletedTask;
        });
        await lifecycle.MarkEnabledAsync();
        await lifecycle.StartAsync();
        events.Clear();

        await lifecycle.DisableAsync();

        CollectionAssert.AreEqual(new[] { "revoke", "stop" }, events);
        Assert.IsFalse(lifecycle.Current.McpEnabled);
        Assert.AreEqual(ManagedConnectionProviderState.Stopped, lifecycle.Current.ProviderState);
    }

    [TestMethod]
    public async Task ShutdownRevokesBeforeStopAndRejectsFurtherUse()
    {
        var events = new List<string>();
        var provider = new FakeProvider(events);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            events.Add("revoke");
            return ValueTask.CompletedTask;
        });
        await lifecycle.MarkEnabledAsync();
        await lifecycle.StartAsync();
        events.Clear();

        await lifecycle.ShutdownAsync();

        CollectionAssert.AreEqual(new[] { "revoke", "stop" }, events);
        Assert.IsFalse(lifecycle.Current.McpEnabled);
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(() => lifecycle.StartAsync());
    }

    [TestMethod]
    public async Task RevokeFailureStillStopsProviderAndExposesOnlyBoundedCode()
    {
        const string secret = "PRIVATE_REVOKE_FAILURE";
        var events = new List<string>();
        var provider = new FakeProvider(events);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            events.Add("revoke");
            throw new InvalidOperationException(secret);
        });
        await lifecycle.MarkEnabledAsync();
        await lifecycle.StartAsync();
        events.Clear();

        var failure = await Assert.ThrowsExceptionAsync<ManagedConnectionException>(
            () => lifecycle.DisableAsync());

        CollectionAssert.AreEqual(new[] { "revoke", "stop" }, events);
        Assert.AreEqual(ManagedConnectionErrors.RevokeFailed, failure.Code);
        Assert.IsFalse(failure.ToString().Contains(secret, StringComparison.Ordinal));
        Assert.AreEqual(ManagedConnectionProviderState.Faulted, lifecycle.Current.ProviderState);
    }

    [TestMethod]
    public async Task StartFailureCleansUpAndDoesNotExposeProviderException()
    {
        const string secret = "SUPER_SECRET_PROVIDER_VALUE";
        var events = new List<string>();
        var provider = new FakeProvider(events) { StartFailure = new InvalidOperationException(secret) };
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ => ValueTask.CompletedTask);
        await lifecycle.MarkEnabledAsync();

        var failure = await Assert.ThrowsExceptionAsync<ManagedConnectionException>(
            () => lifecycle.StartAsync());

        Assert.AreEqual(ManagedConnectionErrors.ProviderStartFailed, failure.Code);
        Assert.IsFalse(failure.ToString().Contains(secret, StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "start", "stop" }, events);
        Assert.AreEqual(ManagedConnectionProviderState.Faulted, lifecycle.Current.ProviderState);
        Assert.AreEqual(ManagedConnectionErrors.ProviderStartFailed, lifecycle.Current.ErrorCode);
    }

    [TestMethod]
    public async Task RepeatedEnableDisableDoesNotAccumulateProviderStarts()
    {
        var provider = new FakeProvider([]);
        int revocations = 0;
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            revocations++;
            return ValueTask.CompletedTask;
        });

        for (int i = 0; i < 3; i++)
        {
            await lifecycle.MarkEnabledAsync();
            await lifecycle.StartAsync();
            await lifecycle.DisableAsync();
        }

        Assert.AreEqual(3, provider.StartCount);
        Assert.AreEqual(3, provider.StopCount);
        Assert.AreEqual(3, revocations);
        Assert.AreEqual(0, provider.ActiveCount);
    }

    [TestMethod]
    public async Task DisabledLifecycleRejectsProviderStart()
    {
        var provider = new FakeProvider([]);
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ => ValueTask.CompletedTask);

        var failure = await Assert.ThrowsExceptionAsync<ManagedConnectionException>(
            () => lifecycle.StartAsync());

        Assert.AreEqual(ManagedConnectionErrors.McpDisabled, failure.Code);
        Assert.AreEqual(0, provider.StartCount);
    }

    private sealed class FakeProvider(List<string> events) : IMcpConnectionProvider
    {
        public string Id => "test-provider";
        public Exception? StartFailure { get; init; }
        public int StartCount { get; private set; }
        public int RefreshCount { get; private set; }
        public int StopCount { get; private set; }
        public int ActiveCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("start");
            StartCount++;
            if (StartFailure is not null) throw StartFailure;
            ActiveCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("refresh");
            RefreshCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            events.Add("stop");
            StopCount++;
            ActiveCount = 0;
            return ValueTask.CompletedTask;
        }
    }
}
