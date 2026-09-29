param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [Alias('Rid')]
    [string] $RuntimeIdentifier,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$manifestPath = Join-Path $root 'native/Cargo.toml'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Cargo manifest not found: $manifestPath"
}
$contracts = @{
    'win-x64' = @{ Target = 'x86_64-pc-windows-msvc'; File = 'cedarsharp_native.dll' }
    'linux-x64' = @{ Target = 'x86_64-unknown-linux-gnu'; File = 'libcedarsharp_native.so' }
    'osx-arm64' = @{ Target = 'aarch64-apple-darwin'; File = 'libcedarsharp_native.dylib' }
}
$contract = $contracts[$RuntimeIdentifier]
if ($RuntimeIdentifier -eq 'win-x64' -and -not $IsWindows -and -not $env:CEDARSHARP_ALLOW_CROSS_COMPILE) {
    throw 'Build win-x64 on a Windows runner with MSVC. Cross compilation is disabled by default.'
}
$rustVersion = (& rustc --version).Trim()
if ($LASTEXITCODE -ne 0 -or $rustVersion -notmatch '^rustc 1\.94\.0(?: |$)') {
    throw "Expected pinned Rust 1.94.0; found '$rustVersion'."
}

$cargoTarget = Join-Path $root 'native/target'
$env:CARGO_TARGET_DIR = $cargoTarget
& cargo build --release --locked --manifest-path $manifestPath --target $contract.Target
if ($LASTEXITCODE -ne 0) { throw "Cargo build failed for $($contract.Target)." }
$builtLibrary = Join-Path $cargoTarget "$($contract.Target)/release/$($contract.File)"
if (-not (Test-Path -LiteralPath $builtLibrary -PathType Leaf)) {
    throw "Expected native library was not produced: $builtLibrary"
}

$metadataText = & cargo metadata --locked --manifest-path $manifestPath --format-version 1 --filter-platform $contract.Target
if ($LASTEXITCODE -ne 0) { throw 'cargo metadata failed.' }
$metadata = ($metadataText -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable
$rootPackage = @($metadata.packages | Where-Object { $_.name -eq 'cedarsharp_native' }) | Select-Object -First 1
if ($null -eq $rootPackage) { throw 'Cargo metadata does not contain cedarsharp_native.' }
$cedarPackage = @($metadata.packages | Where-Object { $_.name -eq 'cedar-policy' }) | Select-Object -First 1
if ($null -eq $cedarPackage) { throw 'Cargo metadata does not include cedar-policy.' }
$cedarVcsFile = Join-Path (Split-Path -Parent $cedarPackage.manifest_path) '.cargo_vcs_info.json'
if (-not (Test-Path -LiteralPath $cedarVcsFile -PathType Leaf)) { throw 'Published Cedar source lacks VCS metadata.' }
$cedarVcs = Get-Content -Raw -LiteralPath $cedarVcsFile | ConvertFrom-Json
if ($cedarVcs.git.sha1 -cne '324d3c09fc94ab91464ec340eb81e4b167deb6f5') { throw 'Published Cedar source commit differs from pinned baseline.' }
$lockText = Get-Content -Raw -LiteralPath (Join-Path $root 'native/Cargo.lock')
$cedarLockEntry = '(?m)^\[\[package\]\]\r?\nname = "cedar-policy"\r?\nversion = "4\.13\.0"\r?\nsource = "registry\+https://github\.com/rust-lang/crates\.io-index"\r?\nchecksum = "5e4f5e46c425491b7133eecb3030d82d27301b84bfa08533c59a6902cca7eb80"'
if ($lockText -notmatch $cedarLockEntry) { throw 'Cargo.lock Cedar crate checksum differs from pinned baseline.' }
$cedarNode = @($metadata.resolve.nodes | Where-Object { $_.id -eq $cedarPackage.id }) | Select-Object -First 1
if ($null -eq $cedarNode) { throw 'Cargo metadata has no resolved cedar-policy feature node.' }
$features = @($cedarNode.features | Sort-Object -Unique)
if (Compare-Object @('datetime', 'decimal', 'ipaddr') $features) { throw 'Unexpected Cedar features in resolved Cargo graph.' }

$lockPath = Join-Path $root 'native/Cargo.lock'
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) { throw 'Cargo.lock is required.' }
$lockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash.ToLowerInvariant()

$nativeRoot = Join-Path $root "native/staging/$RuntimeIdentifier/native"
$fullNativeRoot = [IO.Path]::GetFullPath($nativeRoot)
$relativeNativeRoot = [IO.Path]::GetRelativePath($root, $fullNativeRoot)
if ([IO.Path]::IsPathRooted($relativeNativeRoot) -or $relativeNativeRoot -eq '..' -or $relativeNativeRoot.StartsWith("..$([IO.Path]::DirectorySeparatorChar)")) {
    throw 'Refusing to stage files outside the repository.'
}
if (Test-Path -LiteralPath $nativeRoot) { Remove-Item -LiteralPath $nativeRoot -Recurse -Force }
New-Item -ItemType Directory -Path $nativeRoot -Force | Out-Null
Copy-Item -LiteralPath $builtLibrary -Destination (Join-Path $nativeRoot $contract.File)

$licenseRoot = Join-Path $nativeRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$licenseRecords = [Collections.Generic.List[object]]::new()
foreach ($package in @($metadata.packages | Where-Object { $null -ne $_.source } | Sort-Object name, version)) {
    $packageRoot = Split-Path -Parent $package.manifest_path
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($package.license_file)) {
        $candidates.Add((Join-Path $packageRoot $package.license_file))
    }
    if ($candidates.Count -eq 0) {
        foreach ($pattern in @('LICENSE', 'LICENSE.*', 'LICENSE-*', 'LICENCE', 'LICENCE.*', 'LICENCE-*', 'COPYING', 'COPYING.*', 'COPYING-*', 'NOTICE', 'NOTICE.*', 'NOTICE-*')) {
            foreach ($file in @(Get-ChildItem -LiteralPath $packageRoot -Filter $pattern -File -ErrorAction SilentlyContinue)) {
                $candidates.Add($file.FullName)
            }
        }
    }
    $existing = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Sort-Object -Unique)
    if ($existing.Count -eq 0 -and @('Apache-2.0', 'MIT OR Apache-2.0') -contains $package.license) {
        # Some published crates omit the workspace license. Cedar's pinned
        # Apache text satisfies their declared Apache-2.0 license option.
        $existing = @((Join-Path $root 'native/legal/cedar-LICENSE'))
    }
    if ($package.name -in @('cedar-policy', 'cedar-policy-core', 'cedar-policy-formatter')) {
        $existing += Join-Path $root 'native/legal/cedar-NOTICE'
    }
    if ($existing.Count -eq 0) {
        throw "No license/notice file found in registry package '$($package.name)@$($package.version)' ($($package.license))."
    }
    $safeName = ($package.name -replace '[^A-Za-z0-9._-]', '_') + '@' + ($package.version -replace '[^A-Za-z0-9._-]', '_')
    $packageLicenseRoot = Join-Path $licenseRoot $safeName
    New-Item -ItemType Directory -Path $packageLicenseRoot -Force | Out-Null
    foreach ($licenseFile in $existing) {
        Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $packageLicenseRoot (Split-Path -Leaf $licenseFile))
    }
    $licenseRecords.Add([pscustomobject]@{ name = $package.name; version = $package.version; license = $package.license; files = @($existing | ForEach-Object { [IO.Path]::GetRelativePath($nativeRoot, (Join-Path $packageLicenseRoot (Split-Path -Leaf $_))) -replace '\\', '/' }) })
}
if ($licenseRecords.Count -eq 0) { throw 'Cargo metadata returned no registry package license records.' }
$notices = [Collections.Generic.List[string]]::new()
$notices.Add('# CedarSharp native dependency notices')
$notices.Add('')
$notices.Add('Generated from Cargo metadata for this native build. License documents are included under `licenses/`.')
$notices.Add('')
foreach ($record in $licenseRecords) {
    $files = @($record.files | ForEach-Object { '`' + $_ + '`' }) -join ', '
    $notices.Add("- $($record.name) $($record.version) — $($record.license); $files")
}
[IO.File]::WriteAllText((Join-Path $nativeRoot 'THIRD_PARTY_NOTICES.md'), ($notices -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$licenseInventory = $licenseRecords | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $nativeRoot 'licenses.json'), $licenseInventory + "`n", [Text.UTF8Encoding]::new($false))

$libraryHash = (Get-FileHash -LiteralPath (Join-Path $nativeRoot $contract.File) -Algorithm SHA256).Hash.ToLowerInvariant()
$sourcePaths = [Collections.Generic.List[string]]::new()
$sourcePaths.AddRange([string[]]@((Get-ChildItem -LiteralPath (Join-Path $root 'native/src') -Recurse -File | ForEach-Object FullName)))
$sourcePaths.AddRange([string[]]@((Get-ChildItem -LiteralPath (Join-Path $root 'native/legal') -Recurse -File | ForEach-Object FullName)))
foreach ($path in @('native/Cargo.toml', 'native/build.rs', 'rust-toolchain.toml')) {
    $candidate = Join-Path $root $path
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $sourcePaths.Add($candidate) }
}
$sourceFiles = @($sourcePaths | ForEach-Object { Get-Item -LiteralPath $_ } | Sort-Object FullName)
if ($sourceFiles.Count -eq 0) { throw 'No Rust bridge sources found under native/src.' }
$sourceRecords = foreach ($file in $sourceFiles) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$relative`0$hash`n"
}
$sourceBytes = [Text.UTF8Encoding]::new($false).GetBytes(($sourceRecords -join ''))
$sourceHasher = [Security.Cryptography.SHA256]::Create()
try { $sourceHash = [Convert]::ToHexString($sourceHasher.ComputeHash($sourceBytes)).ToLowerInvariant() } finally { $sourceHasher.Dispose() }
$sourceCommit = '324d3c09fc94ab91464ec340eb81e4b167deb6f5'
$nativeManifest = [ordered]@{
    abiVersion = 1
    sdkVersion = '4.13.0'
    bridgeVersion = '0.1.0'
    rustVersion = '1.94.0'
    target = $contract.Target
    rid = $RuntimeIdentifier
    sha256 = $libraryHash
    sourceCommit = $sourceCommit
    lockSha256 = $lockHash
    bridgeSourceSha256 = $sourceHash
    features = $features
}
$manifestJson = $nativeManifest | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $nativeRoot 'cedarsharp-native.json'), $manifestJson + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Staged $RuntimeIdentifier native library and manifest: $nativeRoot"
