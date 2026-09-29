param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $OutputDirectory = 'artifacts/packages'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet pack (Join-Path $root 'src/CedarSharp/CedarSharp.csproj') --configuration Release --output $output '-p:CedarSharpEnablePack=true' '-p:PackageVersion=0.1.0-preview.1'
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed.' }
$package = Join-Path $output 'CedarSharp.0.1.0-preview.1.nupkg'
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Expected package not found: $package" }
& (Join-Path $PSScriptRoot 'Verify-NuGetPackage.ps1') -PackagePath $package
if ($LASTEXITCODE -ne 0) { throw 'NuGet package verification failed.' }
Write-Host "Package ready: $package"
