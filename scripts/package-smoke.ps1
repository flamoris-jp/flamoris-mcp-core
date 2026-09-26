$ErrorActionPreference = 'Stop'
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed ($LASTEXITCODE)" }
}
$core = 'src/Flamoris.Mcp.Core/Flamoris.Mcp.Core.csproj'
$props = [xml](Get-Content 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Missing package version in Directory.Build.props' }

Invoke-Dotnet build $core -c Release --no-restore --no-incremental
$before = (Get-FileHash 'src/Flamoris.Mcp.Core/bin/Release/net10.0/Flamoris.Mcp.Core.dll').Hash
Invoke-Dotnet build $core -c Release --no-restore --no-incremental
$after = (Get-FileHash 'src/Flamoris.Mcp.Core/bin/Release/net10.0/Flamoris.Mcp.Core.dll').Hash
if ($before -ne $after) { throw 'Non-deterministic Core binary' }
Invoke-Dotnet pack $core -c Release --no-build -o artifacts/packages
$packagePath = Join-Path $PWD 'artifacts/packages'
New-Item -ItemType Directory -Force artifacts/consumer | Out-Null
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Flamoris.Mcp.Core" Version="$version" /></ItemGroup>
</Project>
"@ | Set-Content artifacts/consumer/Consumer.csproj
'new Flamoris.Mcp.Core.McpOptions().Validate(); System.Console.WriteLine("PackageReference smoke passed");' |
    Set-Content artifacts/consumer/Program.cs
@"
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packagePath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="Flamoris.Mcp.Core" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content artifacts/consumer/NuGet.config
Invoke-Dotnet run --project artifacts/consumer/Consumer.csproj -c Release
