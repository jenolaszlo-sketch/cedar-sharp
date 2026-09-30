# Verification and release gates

Updated 2026-09-30. Both
[`0.1.0-preview.1`](https://www.nuget.org/packages/CedarSharp/0.1.0-preview.1)
and [`0.2.0-preview.1`](https://www.nuget.org/packages/CedarSharp/0.2.0-preview.1)
are published on NuGet. `0.2.0-preview.1` has a downloadable symbols package.
Its [release run](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36664097224)
passed all verification jobs but was marked failed by a second, duplicate
symbols upload after both packages had already been accepted. Do not rerun
publishing for this immutable version.

## Initial local Windows x64 baseline

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
No toolchain was installed system-wide by the initial local verification.

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
| Release | Both preview versions are listed on NuGet. Release run #2's failed conclusion reflects a duplicate symbols push after successful uploads. Future releases use the corrected workflow. |
| Safety | Add process-level memory/leak instrumentation, stress beyond the current repeated calls, and adversarial malformed wire fuzzing before making stronger native robustness claims. C-ABI callers must honor readable pointer/length and single-free preconditions. |
| Deployment modes | NativeAOT smoke passed on all three CI RIDs at `a09d6f6`. AOT and trimming analyzers are enabled. Single-file publish, other RIDs and older OS baselines require separate evidence. |
| Hufu | Hufu-specific fail-closed adapter, authority versioning and resource enforcement are separate consumer work. |

## 2026-09-30 graduation pass (source-level, Windows x64)

The graduation review findings F1-F8 were addressed in source. Local evidence:

| Check | Evidence |
| --- | --- |
| Managed build | `dotnet build CedarSharp.slnx -c Release`: zero warnings/errors with `IsAotCompatible`, trim and single-file analyzers enabled on the library. |
| Managed suite | 32/32 on .NET 8.0.31 and 32/32 on .NET 10.0.12, Windows x64. Adds strict malformed-response decoding, Cedar-grammar UID acceptance against the engine, empty-id legality, relative native override, typed-input negatives, and telemetry-privacy checks. |
| AOT smoke | `samples/CedarSharp.AotSmoke` builds warning-free under the AOT analyzer and runs framework-dependent on Windows x64 (`CedarSharp NativeAOT smoke passed`). |
| Native | Unchanged pinned asset (SHA-256 `efa39f0413ca2e6eb6ad7884f033bbae6021d91a58be3aaac26cb86b35784b32`). |

## Committed graduation CI

[CI #5](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36659617333)
passed on `a09d6f6` on 2026-09-30. Its three native jobs built and tested
Windows x64, Linux x64, and macOS ARM64 assets, ran the managed suites on .NET 8
and .NET 10, and published and executed the NativeAOT smoke for each RID. The
pack job assembled and verified the three-RID `0.2.0-preview.1` archive and
uploaded the package and matching symbols archive. All six clean package
consumers passed. I downloaded the CI artifact and independently reran
`eng/Verify-NuGetPackage.ps1` against a clean archive of `a09d6f6`; the
three-RID, ABI, binary-hash and license checks passed. I also ran the downloaded
archive through the isolated Windows x64 packaged consumer on .NET 8 and .NET 10;
both RID-published consumers passed. The exact archives are:

| Archive | SHA-256 |
| --- | --- |
| `CedarSharp.0.2.0-preview.1.nupkg` | `0425112f01684501bfc9bf2902110cbb69c9cc8c0af0c319d50b535f3f1c599c` |
| `CedarSharp.0.2.0-preview.1.snupkg` | `053ccaddcc4d185ce5469fe546e171e845a33021b83c751d70383844d06ae865` |

The symbols archive contains the `net8.0` and `net10.0` PDBs. The run's package artifact digest is
`sha256:2b5772bf9defb80645ed52be6f39bbdcf898bb250bd42664d6032a6086d3c597`;
this identifies the GitHub artifact container, not the `.nupkg` bytes.
The manual publishing workflow rebuilds and tests its own package at the
selected commit; these checksums describe CI #5's artifact, not a future
publishing run's output.

This qualifies the tested runner environments and the CI artifact. It does not
qualify older Windows/macOS releases or a lower Linux glibc baseline. Wire
fuzzing, process-level memory instrumentation, a frozen public API contract and
Hufu integration remain open. Hufu integration is a separate consumer task.

## Published `0.2.0-preview.1` release

[Publish run #2](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36664097224)
ran at `456b7aa`. The release check, all three native and NativeAOT jobs, the
package job, all six clean consumers and the final archive verifier passed.
The NuGet login succeeded. `dotnet nuget push` accepted both the `.nupkg` and
its adjacent `.snupkg`; the package and symbols are listed on
[NuGet](https://www.nuget.org/packages/CedarSharp/0.2.0-preview.1).
The subsequent explicit symbols push received HTTP 409 because that same symbols
version was already pending validation, making the overall workflow conclusion
`failure`. The extra push has been removed for future versions. Do not rerun this
version's publication.

I downloaded the release run's package artifact and reran the archive verifier
against a clean archive of `456b7aa`; it passed. The uploaded files have these
SHA-256 checksums:

| Archive | SHA-256 |
| --- | --- |
| `CedarSharp.0.2.0-preview.1.nupkg` | `c00bdfcea825a21333e787479648a9fc268ac2dd84f3220ed5af16ecdf5ec378` |
| `CedarSharp.0.2.0-preview.1.snupkg` | `5a0db77f15b8047fdd74c9fb9f59a741679c9a8d563b92e915d5a0ccc6fa29ee` |

The published package README is the immutable snapshot from `456b7aa` and
still says the release was prepared. This repository record reflects the
observed publication.

The wrapper deliberately preserves Cedar's Allow with policy diagnostics. It
never turns a parsing, bridge or loading failure into a successful decision.
The tests establish semantic and boundary behavior for their fixtures; they do
not establish Hufu resource confinement or a general policy implication proof.

## 1.0.0 candidate and completed CI

The 1.0 candidate fixes `AuthorizeBatch` so every enumerable, including an
`IReadOnlyList`, is snapshotted before evaluation. The public contract is
recorded in [api-contract.md](api-contract.md). The .NET SDK package validation
gate compares both target frameworks with the published `0.2.0-preview.1`
package. A local Windows-only `dotnet pack` completed that validation for the
`1.0.0` candidate; it is not the three-RID distributable archive.

On Windows x64, 7/7 pinned Rust tests passed, including 4,096 deterministic
mutated wire inputs per run with owned JSON output and no native panic status.
The managed suite passed 34/34 on .NET 8.0.31 and 34/34 on .NET 10.0.12. The
new tests cover batch snapshot behavior and representative nullable API
annotations.

[CI run 36725177455](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36725177455)
passed at `4db8081`. All three native jobs ran 7/7 Rust tests, both managed
target frameworks, and NativeAOT smoke. The package job assembled and verified
the three-RID `1.0.0` archive; all six clean package consumers passed. I
downloaded the package artifact and reran `eng/Verify-NuGetPackage.ps1` against
a clean archive of `4db8081`; its RID inventory, ABI, binary hashes, and
license inventories passed. The CI archives have these SHA-256 values:

| Archive | SHA-256 |
| --- | --- |
| `CedarSharp.1.0.0.nupkg` | `f01a361e9ff1b656a38fc21d3eaa1d3e6dd67cc3aefd22bbbc6d27fde6f85542` |
| `CedarSharp.1.0.0.snupkg` | `17f5f2cc1470e4b5c596440f70c06f9049c0f3cfaa1c04d2fabf36a7b9e09b8c` |

The Linux Valgrind run exercised 256 deterministic wire cases, reported zero
definitely lost bytes and zero errors, and reported 12,624 bytes in 50 blocks
as possibly lost. The recorded allocation traces include the pinned Cedar
engine's extension initialization. This check does not prove absence of all
native leaks or establish a bound on long-running memory use.

The chosen 1.0 deployment scope is Windows Server 2025 x64, Ubuntu 24.04 x64,
and macOS 15 ARM64 as tested by CI, on .NET 8 and .NET 10. This is not a claim
about earlier OS releases, lower glibc versions, musl, other architectures, or
single-file publishing.
