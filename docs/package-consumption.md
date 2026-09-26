# Consuming Flamoris.Mcp.Core

FLAMORIS applications should consume MCP Core as a NuGet package. Do not vendor or copy `Flamoris.Mcp.Core.dll` into application repositories.

## Package

- Package ID: `Flamoris.Mcp.Core`
- Current version: `1.1.0`
- Target framework: `.NET 10`
- Feed: `https://api.nuget.org/v3/index.json`

The package depends on `Flamoris.Logging 1.0.0` and the official MCP C# SDK, also restored through nuget.org.

## Local development

`Flamoris.Mcp.Core` is published publicly on nuget.org. Ordinary restore does not require GitHub authentication, a PAT, or a FLAMORIS-specific package source.

Reference the stable package normally:

~~~xml
<ItemGroup>
  <PackageReference Include="Flamoris.Mcp.Core" Version="1.1.0" />
</ItemGroup>
~~~

With the standard nuget.org source enabled:

~~~powershell
dotnet restore
~~~

## Bridge runtime

`Flamoris.Mcp.Core` is the shared library. The companion `Flamoris.Mcp.Bridge.exe` is a self-contained Windows runtime used by the standard desktop connection shape:

~~~text
external MCP client -> stdio bridge -> same-user named pipe -> running application
~~~

The bridge is not a second editor and owns no document/session state.

The release workflow produces and tests a self-contained win-x64 bridge before the Core package is pushed. Host application packaging remains responsible for shipping the matching bridge runtime alongside the product where required. Do not replace it with an application-specific second authority.

## GitHub Actions consumers

Consumer workflows need no package-read permission or NuGet credential for `Flamoris.Mcp.Core`.

~~~yaml
permissions:
  contents: read

steps:
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: 10.0.x

  - run: dotnet restore
~~~

## Publishing a new version

Publishing is normally tag-driven.

1. Update `<Version>` in `Directory.Build.props`.
2. Merge the reviewed release change to `main`.
3. Create the exact matching tag, for example `v1.1.1`.
4. The publish workflow verifies that the tag belongs to reviewed `main` history and matches the package version.
5. The workflow restores, builds, produces the self-contained bridge, runs the official-client transport tests, performs deterministic/package smoke checks, obtains a short-lived nuget.org API key through Trusted Publishing (GitHub Actions OIDC), and pushes `Flamoris.Mcp.Core` to nuget.org.

The workflow also supports a guarded manual dispatch from the exact current `main` commit for feed migration or release recovery. Normal releases should use matching version tags.

Package versions are immutable. The workflow intentionally does not use `--skip-duplicate`.

Do not create release tags from unreviewed feature branches.
