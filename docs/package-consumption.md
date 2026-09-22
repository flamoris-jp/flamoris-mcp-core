# Consuming Flamoris.Mcp.Core

FLAMORIS applications should consume MCP Core as a NuGet package. Do not vendor or copy `Flamoris.Mcp.Core.dll` into application repositories.

## Package

- Package ID: `Flamoris.Mcp.Core`
- Current version: `1.1.0`
- Target framework: `.NET 10`
- Feed: `https://nuget.pkg.github.com/flamoris-jp/index.json`

The package depends on `Flamoris.Logging 1.0.0` and the official MCP C# SDK.

## Local development

GitHub Packages requires authentication for private organization packages.

Configure the FLAMORIS feed with a GitHub credential that can read packages. Keep credentials outside source control.

Example:

~~~xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="flamoris" value="https://nuget.pkg.github.com/flamoris-jp/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
~~~

Then reference the stable package:

~~~xml
<ItemGroup>
  <PackageReference Include="Flamoris.Mcp.Core" Version="1.1.0" />
</ItemGroup>
~~~

Package access is controlled by GitHub package/repository permissions. Grant consumer repositories read access to the package instead of copying binaries or committing long-lived credentials.

## Bridge runtime

`Flamoris.Mcp.Core` is the shared library. The companion `Flamoris.Mcp.Bridge.exe` is a self-contained Windows runtime used by the standard desktop connection shape:

~~~text
external MCP client -> stdio bridge -> same-user named pipe -> running application
~~~

The bridge is not a second editor and owns no document/session state.

The release workflow produces and tests a self-contained win-x64 bridge before the Core package is pushed. Host application packaging remains responsible for shipping the matching bridge runtime alongside the product where required. Do not replace it with an application-specific second authority.

## GitHub Actions consumers

A consumer workflow can configure the package source through `actions/setup-dotnet`:

~~~yaml
permissions:
  contents: read
  packages: read

steps:
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: 10.0.x
      source-url: https://nuget.pkg.github.com/flamoris-jp/index.json
    env:
      NUGET_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}

  - run: dotnet restore
    env:
      NUGET_AUTH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
~~~

## Publishing a new version

Publishing is tag-driven.

1. Update `<Version>` in `Directory.Build.props`.
2. Merge the reviewed release change to `main`.
3. Create the exact matching tag, for example `v1.1.0`.
4. The publish workflow verifies that the tag belongs to reviewed `main` history and matches the package version.
5. The workflow restores, builds, produces the self-contained bridge, runs the official-client transport tests, performs deterministic/package smoke checks, packs `Flamoris.Mcp.Core`, and pushes the package to GitHub Packages.

Package versions are immutable. The workflow intentionally does not use `--skip-duplicate`.

Do not create release tags from unreviewed feature branches.
