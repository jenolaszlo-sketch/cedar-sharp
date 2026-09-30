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
- [x] Run and pass the three-platform native and package CI matrix.
- [x] Verify the dependency/license inventory in the assembled CI package.
- [ ] Qualify minimum OS and glibc versions beyond the CI runners.
- [x] Establish a versioned release and NuGet publication process (manual
      trusted-publishing workflow; first preview published by the maintainer).
- [x] Advertise trimming and NativeAOT: the library is `IsAotCompatible` and CI
      publishes and runs a NativeAOT consumer per RID. Single-file is exercised
      by RID publish; oldest-OS qualification remains open.

The initial Windows, Linux and macOS runner matrix is qualified for the tested
environments. A `0.1.0-preview.1` package was published; the graduation pass
below prepares `0.2.0-preview.1`.

## M2.5: graduation hardening (2026-09-30)

- [x] Strict response decoding: required arrays and malformed answers fail closed.
- [x] Cedar-grammar entity UID parse/format with differential tests against Cedar.
- [x] Distinct `CedarAuthorizationException`/`CedarValidationException`; clean Deny
      accepted by `EnsureNoErrors`, rejected by `RequireAllow`.
- [x] Legal empty Cedar ids; null rejected.
- [x] Relative `CEDARSHARP_NATIVE_PATH` resolves the adjacent manifest; tests and
      packaged consumers isolate the override.
- [x] Duplicate policy ids, null parents/entities and total `PolicyIds` formatting.
- [x] Trim/NativeAOT-compatible public surface with `JsonTypeInfo`/`JsonElement`
      paths; reflection helpers annotated.
- [x] SourceLink symbols, hardened archive verification, RID-published consumer.
- [ ] Public API compatibility baseline and exception/nullability freeze.
- [ ] Minimum Windows/macOS/glibc baselines executed on oldest intended systems.
- [ ] Wire-boundary fuzzing and process-level memory instrumentation.
- [ ] Hufu fail-closed adapter validated end-to-end.


## M3: consumers and performance

- [ ] Integrate in Penghou.Hufu.Cedar as a separate task; preserve strict host semantics.
- [ ] Measure realistic repeated authorization before adding immutable parsed handles.
- [ ] Establish stable API compatibility baselines and an engine upgrade procedure.

## M4: optional analysis

- [ ] Evaluate CedarSharp.Analysis over upstream SymCC with explicit assumptions,
      solver identity, timeouts/unknown outcomes, limits and counterexamples.

Analysis is separate from runtime authorization and its package dependencies.
