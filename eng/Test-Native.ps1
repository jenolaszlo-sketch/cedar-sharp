param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
$contracts = @{
    'win-x64' = 'x86_64-pc-windows-msvc'
    'linux-x64' = 'x86_64-unknown-linux-gnu'
    'osx-arm64' = 'aarch64-apple-darwin'
}
$manifestPath = Join-Path ([IO.Path]::GetFullPath($RepositoryRoot)) 'native/Cargo.toml'
$env:CARGO_TARGET_DIR = Join-Path ([IO.Path]::GetFullPath($RepositoryRoot)) 'native/target'
& cargo test --locked --manifest-path $manifestPath --target $contracts[$RuntimeIdentifier]
if ($LASTEXITCODE -ne 0) { throw "Cargo tests failed for $RuntimeIdentifier." }
