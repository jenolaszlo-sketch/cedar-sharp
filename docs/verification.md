# Verification and release gates

Updated 2026-09-29. The initial wrapper has been exercised on this Windows x64
host. Linux x64 and macOS ARM64 are CI targets and have not yet run; there is no
qualified three-RID package or published release.

## Executed on Windows x64

| Check | Evidence |
| --- | --- |
| Baseline | Cedar crate 4.13.0 (`5e4f5e46c425491b7133eecb3030d82d27301b84bfa08533c59a6902cca7eb80`), pinned source commit `324d3c09fc94ab91464ec340eb81e4b167deb6f5`, Rust 1.94.0, ABI 1, language 4.5. |
| Native | `cargo test --locked --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc`: 6 passed. Includes direct comparison to Cedar FFI, default deny/permit/forbid, hierarchy, Allow-with-errors, parsing/validation, malformed UTF-8/JSON, pointer/length limits and concurrency. |
| Staging | `eng/Build-Native.ps1 -RuntimeIdentifier win-x64` and `eng/Verify-NativeStaging.ps1 -RuntimeIdentifier win-x64`: passed. DLL SHA-256 `efa39f0413ca2e6eb6ad7884f033bbae6021d91a58be3aaac26cb86b35784b32`. Manifest records crate source, lockfile, bridge source, features and target. |
| Managed | `dotnet build CedarSharp.slnx -c Release`: zero warnings/errors. Native-backed executable tests: 16/16 passed on .NET 8.0.31 and 16/16 on .NET 10.0.12. Covers results/diagnostics, templates, schema/request/context/entities, Unicode, parse vs evaluation failure, concurrency/repetition and missing/tampered/wrong-ABI assets. |
| Local archive | An explicit Windows-only local package `0.1.0-local.6` passed `eng/Verify-NuGetPackage.ps1 -ExpectedRids win-x64 -ExpectedVersion 0.1.0-local.6`. Exact runtime inventory, binary hash, source/lock identity and dependency license inventory checked. The clean external NuGet consumer passed on .NET 8 and .NET 10 with an isolated package cache. This local package is an ignored test artifact, not a distributable three-platform release. |

The first native build used an isolated repository-local MSVC/Windows SDK and
Rust toolchain because this workstation lacked the C++ linker. Build inputs are
reproducible in the repository; the downloaded tools and binaries are ignored.
No toolchain or package was installed system-wide or published.

## CI distribution gate still required

The [CI workflow](../.github/workflows/ci.yml) builds and runs native plus
managed tests on native Windows x64, Linux x64 and macOS ARM64 runners. It then
assembles only verified native artifacts, verifies their hashes and license
inventories in the actual NuGet archive, and runs clean packaged consumers on
all three RIDs with .NET 8 and .NET 10. The workflow has not executed for this
repository yet. macOS runner selection follows the [GitHub hosted runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners),
which identifies `macos-15` as ARM64; the actual run must prove the target.

| Remaining area | Evidence needed before claim |
| --- | --- |
| Linux/macOS | Native bridge build, direct Rust comparison, managed tests and packaged consumer on each native runner. Confirm glibc/minimum OS versions from produced binaries and runners. |
| Distribution | Three native CI artifacts, full archive verification, clean six-case NuGet consumer matrix. Normal packaging stays opt-in until this passes. |
| Safety | Add process-level memory/leak instrumentation, stress beyond the current repeated calls, and adversarial malformed wire fuzzing before making stronger native robustness claims. C-ABI callers must honor readable pointer/length and single-free preconditions. |
| Deployment modes | NativeAOT, trimming, single-file and other RIDs require their own package consumer evidence. |
| Hufu | Hufu-specific fail-closed adapter, authority versioning and resource enforcement are separate consumer work. |

The wrapper deliberately preserves Cedar's Allow with policy diagnostics. It
never turns a parsing, bridge or loading failure into a successful decision.
The tests establish semantic and boundary behavior for their fixtures; they do
not establish Hufu resource confinement or a general policy implication proof.
