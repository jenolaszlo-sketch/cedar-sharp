param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $OutputDirectory = 'artifacts/packages'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
[xml] $project = Get-Content -LiteralPath (Join-Path $root 'src/CedarSharp/CedarSharp.csproj')
$version = [string] $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Package version is missing from the project.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet pack (Join-Path $root 'src/CedarSharp/CedarSharp.csproj') --configuration Release --output $output '-p:CedarSharpEnablePack=true'
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed.' }
$package = Join-Path $output "CedarSharp.$version.nupkg"
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Expected package not found: $package" }
& (Join-Path $PSScriptRoot 'Verify-NuGetPackage.ps1') -PackagePath $package -ExpectedVersion $version
if ($LASTEXITCODE -ne 0) { throw 'NuGet package verification failed.' }
Write-Host "Package ready: $package"
