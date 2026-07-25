# Builds Vixen without Visual Studio / the C++ toolchain.
#
# The three native projects (Box2D, QMLibrary, LiquidFunWrapper) need the MSVC toolset, which the
# dotnet CLI alone can't provide. Only two of them produce assemblies that C# consumes, and both are
# C++/CLI, so this build references prebuilt copies from build-native\ instead (see that folder's
# README.md) and stubs the .vcxproj so the solution can still load.
#
# Everything else is compiled from source. Output lands in Debug\Output\Vixen.Application.exe.
#
# Usage:   .\build.ps1              (Debug|x64)
#          .\build.ps1 -Configuration Release

param(
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Stub C++ targets so the .vcxproj files can be LOADED by the dotnet CLI (they are never built).
$stub = Join-Path $env:TEMP 'vixen-vcstub'
New-Item -ItemType Directory -Force -Path $stub | Out-Null
'<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"></Project>' |
    Set-Content -Encoding utf8 (Join-Path $stub 'Microsoft.Cpp.Default.props')
'<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"></Project>' |
    Set-Content -Encoding utf8 (Join-Path $stub 'Microsoft.Cpp.props')
@'
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Target Name="Build" />
  <Target Name="Rebuild" />
  <Target Name="Clean" />
  <Target Name="GetTargetPath" />
  <Target Name="GetNativeManifest" />
  <Target Name="GetCopyToOutputDirectoryItems" />
  <Target Name="GetTargetFrameworks" />
  <Target Name="GetTargetFrameworkProperties" />
</Project>
'@ | Set-Content -Encoding utf8 (Join-Path $stub 'Microsoft.Cpp.targets')

Write-Host "Building $Configuration|$Platform ..." -ForegroundColor Cyan

& dotnet build (Join-Path $root 'Vixen.sln') `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:SolutionDir="$root\" `
    -p:UsePrebuiltNative=true `
    -p:VCTargetsPath="$stub\" `
    -v minimal --nologo

if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit $LASTEXITCODE }

# Ijwhost.dll is the C++/CLI (IJW) host shim. It is normally deployed by the .vcxproj build we skip;
# without it the mixed-mode LiquidFunWrapper assembly fails to load at runtime with a misleading
# "The specified module could not be found."
$outDir = Join-Path $root "$Configuration\Output"
Copy-Item (Join-Path $root 'build-native\Ijwhost.dll') $outDir -Force

Write-Host ""
Write-Host "BUILD OK -> $outDir\Vixen.Application.exe" -ForegroundColor Green
