using System.Security.Cryptography;
using System.Text;

namespace Flamoris.Mcp.Core;

// No record-generated ToString: this object must never print the credential.
public sealed class CapabilityGrant : IDisposable
{
    internal object Gate { get; } = new();
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    private readonly CancellationTokenSource revoked = new();
    private bool active = true;
    internal CapabilityGrant(HostSnapshot snapshot, McpPermission permission)
    { Snapshot = snapshot; Permission = permission; }
    public HostSnapshot Snapshot { get; }
    public McpPermission Permission { get; }
    public CancellationToken Revoked => revoked.Token;
    public bool IsActive { get { lock (Gate) return active; } }
    public string ExportCredential()
    { lock (Gate) { Demand(); return Convert.ToHexString(secret); } }
    public bool Authenticate(string? credential)
    {
        if (credential is not { Length: 64 }) return false;
        Span<byte> supplied = stackalloc byte[32];
        if (!Convert.TryFromHexString(credential, supplied, out int written) || written != 32) return false;
        lock (Gate) return active && CryptographicOperations.FixedTimeEquals(secret, supplied);
    }
    internal void Demand()
    { if (!active) throw new McpFault(McpErrors.Unauthorized); }
    public void Revoke()
    {
        lock (Gate)
        {
            if (!active) return;
            active = false;
            CryptographicOperations.ZeroMemory(secret);
        }
        // User callbacks cannot keep a credential alive or throw into the host.
        try { revoked.Cancel(); } catch (AggregateException) { }
    }
    public void Dispose() => Revoke();
    public override string ToString() => "CapabilityGrant [redacted]";
}
