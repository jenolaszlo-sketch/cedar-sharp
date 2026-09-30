param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string[]] $ExpectedRids = @('win-x64', 'linux-x64', 'osx-arm64'),
    [string] $ExpectedVersion
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = [IO.Path]::GetFullPath($RepositoryRoot)
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    [xml] $project = Get-Content -LiteralPath (Join-Path $root 'src/CedarSharp/CedarSharp.csproj')
    $ExpectedVersion = [string] $project.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { throw 'Package version is missing from the project.' }
}
$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [IO.Compression.ZipFile]::OpenRead($resolved)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
    if ($nuspecEntries.Count -ne 1) { throw "Package must contain exactly one nuspec, found $($nuspecEntries.Count)." }
    $nuspecEntry = $nuspecEntries[0]
    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($nuspec.package.metadata.id -cne 'CedarSharp') { throw 'Unexpected package ID.' }
    if ($nuspec.package.metadata.version -cne $ExpectedVersion) { throw 'Unexpected package version.' }
    foreach ($required in @('README.md', 'LICENSE', 'NOTICE', 'lib/net8.0/CedarSharp.dll', 'lib/net8.0/CedarSharp.xml', 'lib/net10.0/CedarSharp.dll', 'lib/net10.0/CedarSharp.xml')) {
        if (@($entries | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Package must contain exactly one '$required'." }
    }
    $contracts = @(
        @{ Rid = 'win-x64'; File = 'cedarsharp_native.dll'; Target = 'x86_64-pc-windows-msvc' },
        @{ Rid = 'linux-x64'; File = 'libcedarsharp_native.so'; Target = 'x86_64-unknown-linux-gnu' },
        @{ Rid = 'osx-arm64'; File = 'libcedarsharp_native.dylib'; Target = 'aarch64-apple-darwin' }
    )
    $contracts = @($contracts | Where-Object { $ExpectedRids -contains $_.Rid })
    if ($contracts.Count -ne $ExpectedRids.Count) { throw 'Expected RIDs contain duplicates or unsupported values.' }
    $sourcePaths = [Collections.Generic.List[string]]::new()
    $sourcePaths.AddRange([string[]]@((Get-ChildItem -LiteralPath (Join-Path $root 'native/src') -Recurse -File | ForEach-Object FullName)))
    $sourcePaths.AddRange([string[]]@((Get-ChildItem -LiteralPath (Join-Path $root 'native/legal') -Recurse -File | ForEach-Object FullName)))
    foreach ($path in @('native/Cargo.toml', 'native/build.rs', 'rust-toolchain.toml')) {
        $candidate = Join-Path $root $path
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $sourcePaths.Add($candidate) }
    }
    $sourceFiles = @($sourcePaths | ForEach-Object { Get-Item -LiteralPath $_ } | Sort-Object FullName)
    $sourceRecords = foreach ($file in $sourceFiles) {
        $relative = [IO.Path]::GetRelativePath($root, $file.FullName) -replace '\\', '/'
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$relative`0$hash`n"
    }
    $sourceBytes = [Text.UTF8Encoding]::new($false).GetBytes(($sourceRecords -join ''))
    $sourceHasher = [Security.Cryptography.SHA256]::Create()
    try { $expectedSourceHash = [Convert]::ToHexString($sourceHasher.ComputeHash($sourceBytes)).ToLowerInvariant() } finally { $sourceHasher.Dispose() }
    $expectedLockHash = (Get-FileHash -LiteralPath (Join-Path $root 'native/Cargo.lock') -Algorithm SHA256).Hash.ToLowerInvariant()

    foreach ($contract in $contracts) {
        $base = "runtimes/$($contract.Rid)/native"
        $libraryPath = "$base/$($contract.File)"
        $manifestPath = "$base/cedarsharp-native.json"
        foreach ($required in @($libraryPath, $manifestPath)) {
            if (@($entries | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Package must contain exactly one '$required'." }
        }
        $manifestEntry = @($archive.Entries | Where-Object { $_.FullName -ceq $manifestPath })[0]
        $manifestReader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json } finally { $manifestReader.Dispose() }
        foreach ($pair in @(
            @{ Name = 'abiVersion'; Value = 1 }, @{ Name = 'sdkVersion'; Value = '4.13.0' },
            @{ Name = 'bridgeVersion'; Value = '0.1.0' }, @{ Name = 'rustVersion'; Value = '1.94.0' },
            @{ Name = 'rid'; Value = $contract.Rid }, @{ Name = 'target'; Value = $contract.Target },
            @{ Name = 'sourceCommit'; Value = '324d3c09fc94ab91464ec340eb81e4b167deb6f5' },
            @{ Name = 'lockSha256'; Value = $expectedLockHash }, @{ Name = 'bridgeSourceSha256'; Value = $expectedSourceHash }
        )) {
            if ($manifest.($pair.Name) -cne $pair.Value) { throw "Manifest '$manifestPath' has invalid $($pair.Name)." }
        }
        if ($manifest.sha256 -notmatch '^[0-9a-f]{64}$' -or $null -eq $manifest.features) { throw "Manifest '$manifestPath' is missing native hash/features." }
        if (Compare-Object @('datetime', 'decimal', 'ipaddr') @($manifest.features)) { throw "Manifest '$manifestPath' has unsupported Cedar features." }
        $libraryEntry = @($archive.Entries | Where-Object { $_.FullName -ceq $libraryPath })[0]
        $stream = $libraryEntry.Open()
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $actualHash = [Convert]::ToHexString($hasher.ComputeHash($stream)).ToLowerInvariant() } finally { $hasher.Dispose(); $stream.Dispose() }
        if ($actualHash -cne $manifest.sha256) { throw "Native library hash does not match '$manifestPath'." }

        $thirdPartyBase = "third-party/$($contract.Rid)/native"
        $noticePath = "$thirdPartyBase/THIRD_PARTY_NOTICES.md"
        $inventoryPath = "$thirdPartyBase/licenses.json"
        foreach ($required in @($noticePath, $inventoryPath)) {
            if (@($entries | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Package must contain exactly one '$required'." }
        }
        $inventoryEntry = @($archive.Entries | Where-Object { $_.FullName -ceq $inventoryPath })[0]
        $inventoryReader = [IO.StreamReader]::new($inventoryEntry.Open())
        try { $licenseInventory = @($inventoryReader.ReadToEnd() | ConvertFrom-Json) } finally { $inventoryReader.Dispose() }
        if ($licenseInventory.Count -eq 0) { throw "No dependency license inventory for $($contract.Rid)." }
        foreach ($record in $licenseInventory) {
            if ([string]::IsNullOrWhiteSpace($record.name) -or [string]::IsNullOrWhiteSpace($record.version) -or [string]::IsNullOrWhiteSpace($record.license) -or @($record.files).Count -eq 0) {
                throw "Incomplete license inventory record in '$inventoryPath'."
            }
            foreach ($relativeLicense in $record.files) {
                $licensePath = "$thirdPartyBase/$relativeLicense"
                if (@($entries | Where-Object { $_ -ceq $licensePath }).Count -ne 1) { throw "Package is missing listed license file '$licensePath'." }
            }
        }
        $noticeEntry = @($archive.Entries | Where-Object { $_.FullName -ceq $noticePath })[0]
        $noticeReader = [IO.StreamReader]::new($noticeEntry.Open())
        try { $noticeText = $noticeReader.ReadToEnd() } finally { $noticeReader.Dispose() }
        foreach ($record in $licenseInventory) { if (-not $noticeText.Contains("$($record.name) $($record.version)")) { throw "Notices omit $($record.name)@$($record.version)." } }
    }
    $expectedRuntimeEntries = @()
    foreach ($contract in $contracts) {
        $base = "runtimes/$($contract.Rid)/native"
        $expectedRuntimeEntries += "$base/$($contract.File)"
        $expectedRuntimeEntries += "$base/cedarsharp-native.json"
    }
    $actualNativeEntries = @($entries | Where-Object { $_ -match '^runtimes/' })
    if (Compare-Object ($expectedRuntimeEntries | Sort-Object) ($actualNativeEntries | Sort-Object)) {
        throw 'Package runtime content differs from the expected libraries and adjacent manifests.'
    }
    foreach ($entry in @($entries | Where-Object { $_ -match '^third-party/' })) {
        $allowed = $false
        foreach ($contract in $contracts) {
            if ($entry.StartsWith("third-party/$($contract.Rid)/native/", [StringComparison]::Ordinal)) { $allowed = $true; break }
        }
        if (-not $allowed) { throw "Package contains an unsupported third-party entry: $entry" }
    }
    Write-Host "Verified CedarSharp package RIDs, ABI manifests, hashes, and complete license inventories: $resolved"
}
finally { $archive.Dispose() }
