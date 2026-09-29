# CedarSharp roadmap

Updated 2026-09-29. Implemented source is distinct from qualified native behavior.
See [verification evidence](docs/verification.md) for commands and platform results.

## M0: design and baseline

- [x] Establish an independent wrapper boundary and ADR 0001.
- [x] Review and improve the implementation plan using sibling packaging examples.
- [x] Pin Cedar 4.13.0, Rust 1.94.0, stable features and ABI 1.

## M1: initial wrapper implementation

- [x] Add versioned Rust/C bridge with owned output buffers and structured errors.
- [x] Implement managed authorization, parsing/validation, full diagnostics and versions.
- [x] Add immutable requests, stable policy IDs and JSON template support.
- [x] Add native differential tests and managed integration/loader/concurrency tests.
- [x] Compile the .NET 8 / .NET 10 solution.
- [x] Verify the Windows x64 native bridge and .NET 8/10 consumers; record exact results.

## M2: first distribution gate

- [x] Implement native Windows x64, Linux x64 and macOS ARM64 CI jobs.
- [x] Add manifests, notice staging, archive verification and clean NuGet consumers.
- [ ] Run and pass the three-platform native and package CI matrix.
- [ ] Qualify minimum OS/libc versions and dependency/license inventory.
- [ ] Enable normal packaging after verified distribution evidence exists.
- [ ] Prove trimming, NativeAOT and single-file publishing before advertising them.

Windows development is the local starting point. macOS/Linux qualification is
part of the first usable distribution gate. No binaries have been published.

## M3: consumers and performance

- [ ] Integrate in Penghou.Hufu.Cedar as a separate task; preserve strict host semantics.
- [ ] Measure realistic repeated authorization before adding immutable parsed handles.
- [ ] Establish stable API compatibility baselines and an engine upgrade procedure.

## M4: optional analysis

- [ ] Evaluate CedarSharp.Analysis over upstream SymCC with explicit assumptions,
      solver identity, timeouts/unknown outcomes, limits and counterexamples.

Analysis is separate from runtime authorization and its package dependencies.
