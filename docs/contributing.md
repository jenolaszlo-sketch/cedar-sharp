# Build and contribute

CedarSharp keeps the official Cedar engine behind a small Rust bridge. The managed API must preserve Cedar decisions and diagnostics, including an Allow that carries policy errors. When changing either layer, update the corresponding contract and verification evidence.

## Local development

You need the pinned Rust 1.94.0 toolchain, an OS-native linker/SDK, PowerShell, and the .NET 8 and .NET 10 SDKs. Build the native asset for your host RID before running native-backed managed tests. On Windows x64:

```powershell
pwsh ./eng/Build-Native.ps1 -RuntimeIdentifier win-x64
dotnet build CedarSharp.slnx -c Release
dotnet run --project tests/CedarSharp.Tests -c Release -f net8.0
dotnet run --project tests/CedarSharp.Tests -c Release -f net10.0
```

The supported RIDs are `win-x64`, `linux-x64`, and `osx-arm64`. The CI workflow builds and tests natively on each platform, assembles one package from verified assets, then runs clean consumers on each RID and framework. It also publishes and runs the NativeAOT sample.

Packaging is opt-in. A managed build alone does not qualify a native asset or a distributable package. See [native boundary](native-boundary.md) for asset identity and [verification](verification.md) for the release gates.

## Release process

The [Publish to NuGet workflow](../.github/workflows/publish.yml) runs manually from `main`. It repeats native, package, and clean-consumer verification before using NuGet trusted publishing. A normal push or CI run does not publish. Advance the version in [CedarSharp.csproj](../src/CedarSharp/CedarSharp.csproj), review the [changelog](../CHANGELOG.md) and [verification record](verification.md), then run the release workflow. NuGet versions are immutable.

The 1.0.0 package, symbols, and [GitHub release](https://github.com/jenolaszlo-sketch/cedar-sharp/releases/tag/v1.0.0) are public. Prior release and test details remain in the [verification record](verification.md).

## Design references

- [Architecture](architecture.md): layer ownership and semantic boundaries.
- [Native boundary](native-boundary.md): ABI, ownership, asset identity, and distribution.
- [ADR 0001](decisions/0001-own-cedar-binding.md): why the binding uses the official engine.
- [Implementation plan](implementation-handoff.md) and [graduation review](graduation-review-handoff.md): historical planning and acceptance notes.
