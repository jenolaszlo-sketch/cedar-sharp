param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [Parameter(Mandatory = $true)]
    [ValidateSet('net8.0', 'net10.0')]
    [string] $Framework,
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
$contracts = @{
    'win-x64' = @{ Os = 'Windows'; Arch = 'X64'; Target = 'x86_64-pc-windows-msvc' }
    'linux-x64' = @{ Os = 'Linux'; Arch = 'X64'; Target = 'x86_64-unknown-linux-gnu' }
    'osx-arm64' = @{ Os = 'OSX'; Arch = 'Arm64'; Target = 'aarch64-apple-darwin' }
}
$contract = $contracts[$RuntimeIdentifier]
$hostOs = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } elseif ($IsMacOS) { 'OSX' } else { 'Unsupported' }
$hostArch = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
if ($hostOs -cne $contract.Os -or $hostArch -cne $contract.Arch) {
    throw "Package smoke for $RuntimeIdentifier requires $($contract.Os)/$($contract.Arch), found $hostOs/$hostArch."
}
$env:CEDARSHARP_EXPECT_TARGET = $contract.Target
# A clean consumer must not inherit a developer's native override.
Remove-Item Env:CEDARSHARP_NATIVE_PATH -ErrorAction SilentlyContinue
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$packageDirectory = Split-Path -Parent $package
$smokeSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../samples/CedarSharp.Smoke'))
$work = Join-Path ([IO.Path]::GetTempPath()) "CedarSharp.PackageSmoke-$([Guid]::NewGuid().ToString('N'))"
$projectDirectory = Join-Path $work 'consumer'
$packageCache = Join-Path $work 'packages'
New-Item -ItemType Directory -Path $projectDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $smokeSource '*') -Destination $projectDirectory -Recurse -Force
$config = Join-Path $work 'NuGet.Config'
$source = [System.Security.SecurityElement]::Escape($packageDirectory)
$configContents = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="cedarsharp-artifact" value="$source" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@
[IO.File]::WriteAllText($config, $configContents, [Text.UTF8Encoding]::new($false))
$packageFile = [IO.Path]::GetFileName($package)
if ($packageFile -notmatch '^CedarSharp\.(.+)\.nupkg$') { throw "Unexpected package filename: $packageFile" }
$packageVersion = $Matches[1]
$project = Join-Path $projectDirectory 'CedarSharp.Smoke.csproj'
$publishDirectory = Join-Path $work 'publish'
& dotnet restore $project --configfile $config --packages $packageCache -r $RuntimeIdentifier "-p:CedarSharpPackageVersion=$packageVersion"
if ($LASTEXITCODE -ne 0) { throw "Clean packaged restore failed for $RuntimeIdentifier / $Framework." }
& dotnet publish $project --configuration Release --framework $Framework -r $RuntimeIdentifier --self-contained false `
    --no-restore -o $publishDirectory "-p:CedarSharpPackageVersion=$packageVersion"
if ($LASTEXITCODE -ne 0) { throw "RID publish failed for $RuntimeIdentifier / $Framework." }
& dotnet (Join-Path $publishDirectory 'CedarSharp.Smoke.dll')
if ($LASTEXITCODE -ne 0) { throw "Clean published consumer failed for $RuntimeIdentifier / $Framework." }
Write-Host "Packaged CedarSharp RID-published consumer passed: $RuntimeIdentifier / $Framework."
