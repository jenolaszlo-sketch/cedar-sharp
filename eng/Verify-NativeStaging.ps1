param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [Alias('Rid')]
    [string] $RuntimeIdentifier,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$contracts = @{
    'win-x64' = @{ Target = 'x86_64-pc-windows-msvc'; File = 'cedarsharp_native.dll' }
    'linux-x64' = @{ Target = 'x86_64-unknown-linux-gnu'; File = 'libcedarsharp_native.so' }
    'osx-arm64' = @{ Target = 'aarch64-apple-darwin'; File = 'libcedarsharp_native.dylib' }
}
$base = Join-Path $root "native/staging/$RuntimeIdentifier/native"
$library = Join-Path $base $contracts[$RuntimeIdentifier].File
$manifestFile = Join-Path $base 'cedarsharp-native.json'
$notices = Join-Path $base 'THIRD_PARTY_NOTICES.md'
foreach ($file in @($library, $manifestFile, $notices)) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required staged file missing: $file" } }
$manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
$expected = @{
    abiVersion = 1; sdkVersion = '4.13.0'; bridgeVersion = '0.1.0'; rustVersion = '1.94.0'
    target = $contracts[$RuntimeIdentifier].Target; rid = $RuntimeIdentifier
}
foreach ($key in $expected.Keys) { if ($manifest.$key -cne $expected[$key]) { throw "Staged manifest field '$key' is '$($manifest.$key)' (expected '$($expected[$key])')." } }
$hash = (Get-FileHash -LiteralPath $library -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.sha256 -cne $hash) { throw 'Staged native library SHA-256 differs from manifest.' }
foreach ($key in @('lockSha256', 'bridgeSourceSha256')) { if ($manifest.$key -notmatch '^[0-9a-f]{64}$') { throw "Manifest $key is missing or invalid." } }
if ($manifest.sourceCommit -cne '324d3c09fc94ab91464ec340eb81e4b167deb6f5' -or $null -eq $manifest.features) { throw 'Manifest sourceCommit/features are missing or incorrect.' }
if (Compare-Object @('datetime', 'decimal', 'ipaddr') @($manifest.features)) { throw 'Manifest Cedar feature set differs from the supported baseline.' }
$expectedLockHash = (Get-FileHash -LiteralPath (Join-Path $root 'native/Cargo.lock') -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.lockSha256 -cne $expectedLockHash) { throw 'Manifest lockfile hash differs from Cargo.lock.' }
if (@(Get-ChildItem -LiteralPath (Join-Path $base 'licenses') -Recurse -File -ErrorAction SilentlyContinue).Count -eq 0) { throw 'Staged dependency licenses are missing.' }
Write-Host "Verified staged $RuntimeIdentifier asset: $library"
