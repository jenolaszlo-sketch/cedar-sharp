# Implementation plan

Historical planning record. For current usage, see [getting started](getting-started.md);
for release status, see [verification](verification.md).

Updated 2026-09-29 after review against LatticeDBSharp's native build/package CI
and CactusNeedleSharp's package metadata/content checks. The initial wrapper and
three-platform CI gate are complete; the verification ledger records evidence.

## First usable delivery

1. Pin Cedar 4.13.0, Rust 1.94.0, explicit stable features and Cargo.lock. Record
   crate checksum, immutable source commit and all native dependency notices.
2. Own ABI 1 with pointer/length UTF-8 buffers, bounded messages, native-owned
   release and panic/error containment. Delegate to Cedar's official FFI and
   preserve its complete answers. Compare bridge output with direct Rust calls.
3. Expose immutable managed inputs, stable policy IDs, templates through JSON,
   Cedar/JSON schema forms, typed decisions and full diagnostics with raw JSON.
   Distinguish parse failure, validation errors, policy evaluation errors and
   boundary failure. Document sync/thread-safety/lifetime/resource limits.
4. Verify semantics, malformed input, UTF-8, schema/request/entity/context
   validation, native ownership, concurrent isolation and loaded asset identity.
   Test Allow-with-errors explicitly for Hufu and other strict consumers.
5. Build and run Rust + managed tests on native Windows x64, Linux x64 and macOS
   ARM64 CI runners. Use Cargo --locked and explicit target triples. macOS is
   part of the first distribution gate, not an assumed cross-compile success.
6. Assemble only those verified assets into a NuGet archive with manifests,
   README and licenses. Verify exact asset inventory and hashes; run clean
   package consumers on every RID with .NET 8 and .NET 10. Keep packaging opt-in
   until a release is approved; publishing remains a separate action.

## Acceptance details

- GetVersion reports the loaded engine, language, bridge, ABI, Rust, target,
  features and native hash/path. Never substitute a managed version constant.
- Missing binaries/manifests, hash mismatch, wrong ABI/engine/architecture and
  unsupported RIDs fail clearly. No implicit downloads or loader search fallback.
- Diagnostic source ranges use UTF-8 byte offsets. Arrays corresponding to
  upstream sets have no ordering contract. Preserve related errors and warnings.
- Consumer tests use an actual nupkg, not just a project reference. Platform
  claims require platform execution, including macOS dylib loading.
- Shipping binary provenance includes source/lock hashes and transitive notices.
  Hash manifests check integrity, not publisher authenticity.

## Follow-on work

Measure repeated evaluations on representative policy/entity bundles before
adding parsed handles or caches. A future benchmark should separate serialization,
parsing, evaluation, allocations and concurrency throughput. Any handles require
SafeHandle lifetime/disposal races and immutable snapshot coherence tests.

Qualify trimming, NativeAOT, single-file deployment, additional RIDs and minimum
OS versions separately. Add an API compatibility baseline before a stable public
release. Hufu's adapter and fixtures can then consume this generic package and
apply its stricter diagnostic handling without altering Cedar semantics.

Symbolic analysis and solver lifecycle remain a separate optional package.
NuGet publication and Hufu runtime changes remain separate from the wrapper.
The executed CI matrix is linked in the verification ledger.
