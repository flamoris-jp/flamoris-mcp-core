using System.Text;
using Flamoris.Logging;
using Flamoris.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class DiagnosticsTests
{
    [TestMethod]
    public async Task LoggingOmitsSecretsPayloadAndExceptionMessages()
    {
        TextWriter previous = Console.Out;
        using var capture = new StringWriter();
        Console.SetOut(capture);
        try {
            var host = new HostHarness();
            var bad = new HostTool<int>("secret.test", "Test failure", HostHarness.Json(new { type = "object" }),
                OperationKind.Query, _ => 1, (_, _, _) => throw new Exception("PRIVATE_ARTWORK_AND_SECRET"));
            using var core = host.Boundary(extra: [bad]);
            using var grant = await core.EnableAsync(McpPermission.Edit);
            string secret = grant.ExportCredential();
            await core.InvokeAsync(grant, "secret.test", HostHarness.Json(new { payload = "PRIVATE_ARTWORK_AND_SECRET" }), null);
            string log = capture.ToString();
            StringAssert.Contains(log, "mcp.query");
            Assert.IsFalse(log.Contains(secret));
            Assert.IsFalse(log.Contains("PRIVATE_ARTWORK_AND_SECRET"));
        }
        finally { Console.SetOut(previous); }
    }
    [TestMethod]
    public async Task BrokenLoggingSinkDoesNotAffectMutation()
    {
        TextWriter previous = Console.Out; Console.SetOut(new BrokenWriter());
        try {
            var host = new HostHarness(); using var core = host.Boundary();
            using var grant = await core.EnableAsync(McpPermission.Edit);
            Assert.IsFalse((await core.InvokeAsync(grant, "value.set", HostHarness.Json(new { value = 2 }), host.Guard())).IsError);
            Assert.AreEqual(2, host.Value);
        }
        finally { Console.SetOut(previous); }
    }
    [TestMethod]
    public void CoreHasNoEditorOrWpfAssemblyDependency()
    {
        var references = typeof(McpBoundary).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.IsTrue(references.Contains("Flamoris.Logging"));
        Assert.IsFalse(references.Any(x => x.Contains("Presentation") || x.Contains("Kachinco")
            || x.Contains("Cutwork") || x.Contains("Flamoris2D")));
        Assert.AreEqual("Flamoris.Mcp.Core", typeof(McpBoundary).Assembly.GetName().Name);
    }
    private sealed class BrokenWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => throw new IOException("broken sink");
        public override void Write(string? value) => throw new IOException("broken sink");
    }
}
