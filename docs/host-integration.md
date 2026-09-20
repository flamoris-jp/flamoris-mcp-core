# Host integration

## Implement the facade

Wrap the existing session; do not construct one in the adapter. `Snapshot` exposes
stable product ID, transient runtime ID, transient document token and the host's
existing revision. `InvokeAsync` marshals to the application's one serialization
lane. `Invalidating` fires before replacement/shutdown.

```csharp
public sealed class AppMcpHost : IMcpHost
{
    private readonly ExistingEditorSession session;
    public HostSnapshot Snapshot => ProjectFrom(session);
    public event Action? Invalidating;
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token) =>
        dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); return action(); });
    public void BeforeReplacement() => Invalidating?.Invoke();
}
```

## Register typed tools

Each `HostTool<T>` supplies an exact JSON schema, decoder and adapter to an existing
application command/query. Decode to a closed DTO. Mutations prepare first and
call `context.CommitAsync` once with an existing atomic command/transaction.

Queries call `context.ReadAsync`. Undo and Redo call the application's existing
history through `CommitAsync`. Do not add property-path mutation, file paths,
shell/process calls or a Core-owned history.

## Enable and serve

```csharp
var core = new McpBoundary(host, tools, options, diagnostics);
var grant = await core.EnableAsync(McpPermission.ReadOnly);
var endpoint = new LocalMcpEndpoint(core);
_ = endpoint.RunAsync(grant, applicationLifetime);
```

Changing permission calls `Disable`, then fresh `EnableAsync`. Copy connection
information only through an explicit user action. A launcher passes the exported
credential in `FLAMORIS_MCP_CAPABILITY` and invokes the packaged bridge with
`--pipe <options.PipeName>`. Never save the credential in app settings/project.

## UI

Subscribe to `Status.Changed`, marshal to the UI thread, then reread
`Status.Current`. Render green for `IsGreen`; otherwise red. `Connected` can refine
text but does not identify an AI. Show only `LastError` codes mapped to localized,
user-safe messages.

While `ActivityVisible`, select the documented Chipsy runtime frame/animation.
When it becomes false, restore the cursor appropriate to the current host tool.
Always restore on success, failure, cancel, timeout, disconnect and shutdown.

See `assets/README.md` for canonical asset coordinates and DPI guidance.

## Replacement and failure order

1. Validate/decode the candidate replacement without changing the active document.
2. Once replacement will occur, fire `Invalidating` and wait for revocation.
3. Install the new document/session and update UI.
4. Require explicit fresh enable.

A failed/cancelled open before step 2 may preserve access. Authority/control-channel
loss must fail closed: revoke and stop the endpoint before MCP can become the sole
remaining editor.
