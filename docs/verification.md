# Verification and release gates

Updated 2026-09-29. The wrapper passed local Windows x64 testing and a complete
CI matrix on Windows x64, Linux x64 and macOS ARM64. CI produced and verified a
three-RID package; no package or release has been published.

## Executed on Windows x64

| Check | Evidence |
| --- | --- |
| Baseline | Cedar crate 4.13.0 (`5e4f5e46c425491b7133eecb3030d82d27301b84bfa08533c59a6902cca7eb80`), pinned source commit `324d3c09fc94ab91464ec340eb81e4b167deb6f5`, Rust 1.94.0, ABI 1, language 4.5. |
| Native | `cargo test --locked --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc`: 6 passed. Includes direct comparison to Cedar FFI, default deny/permit/forbid, hierarchy, Allow-with-errors, parsing/validation, malformed UTF-8/JSON, pointer/length limits and concurrency. |
| Staging | `eng/Build-Native.ps1 -RuntimeIdentifier win-x64` and `eng/Verify-NativeStaging.ps1 -RuntimeIdentifier win-x64`: passed. DLL SHA-256 `efa39f0413ca2e6eb6ad7884f033bbae6021d91a58be3aaac26cb86b35784b32`. Manifest records crate source, lockfile, bridge source, features and target. |
| Managed | `dotnet build CedarSharp.slnx -c Release`: zero warnings/errors. Native-backed executable tests: 17/17 passed on .NET 8.0.31 and 17/17 on .NET 10.0.12. Covers results/diagnostics, templates, copied policy snapshots, schema/request/context/entities, Unicode, parse vs evaluation failure, concurrency/repetition and missing/tampered/wrong-ABI assets. |
| Local archive | An explicit Windows-only local package `0.1.0-local.7` passed `eng/Verify-NuGetPackage.ps1 -ExpectedRids win-x64 -ExpectedVersion 0.1.0-local.7`. Exact runtime inventory, binary hash, source/lock identity, dependency license inventory and XML API documentation checked. The clean external NuGet consumer passed on .NET 8 and .NET 10 with an isolated package cache. This local package is an ignored test artifact, not a distributable three-platform release. |

The first native build used an isolated repository-local MSVC/Windows SDK and
Rust toolchain because this workstation lacked the C++ linker. Build inputs are
reproducible in the repository; the downloaded tools and binaries are ignored.
No toolchain or package was installed system-wide or published.

## Completed CI distribution gate

[Run 36553672035](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36553672035)
passed on commit `8ae9b26`. The native jobs ran Rust tests, built and verified
the bridge, and passed the managed integration suite on both .NET 8 and .NET 10
on Windows 2025 x64, Ubuntu 24.04 x64 and macOS 15 ARM64. The package job
assembled `CedarSharp.0.1.0-preview.1.nupkg` from all three verified assets
and checked runtime inventory, ABI manifests, hashes and dependency license
files. Clean consumers passed for each of the three RIDs on both .NET versions
(six combinations). The CI package is a build artifact, not a published release.

The run verifies these native runner environments. It does not establish a
minimum supported Windows or macOS version or a minimum Linux glibc version.

| Remaining area | Evidence needed before claim |
| --- | --- |
| Minimum platform versions | Inspect produced binaries and test oldest intended Windows, macOS and glibc baselines before claiming broader support. |
| Release | Manual trusted-publishing workflow is configured; NuGet policy and `NUGET_USER` secret must be set, then the workflow run must pass before claiming a published release. |
| Safety | Add process-level memory/leak instrumentation, stress beyond the current repeated calls, and adversarial malformed wire fuzzing before making stronger native robustness claims. C-ABI callers must honor readable pointer/length and single-free preconditions. |
| Deployment modes | NativeAOT, trimming, single-file and other RIDs require their own package consumer evidence. |
| Hufu | Hufu-specific fail-closed adapter, authority versioning and resource enforcement are separate consumer work. |

The wrapper deliberately preserves Cedar's Allow with policy diagnostics. It
never turns a parsing, bridge or loading failure into a successful decision.
The tests establish semantic and boundary behavior for their fixtures; they do
not establish Hufu resource confinement or a general policy implication proof.
