# ADR 0001: Own a thin .NET binding over the official Cedar engine

Date: 2026-09-28.

Status: **Accepted architectural direction; initial implementation in progress.**

## Context

Hufu needs policy evaluation with explicit prohibition, hierarchy, validation,
and attributable diagnostics. Cedar supplies these semantics. CedarDotNet
demonstrates a small Rust-to-.NET bridge, but owning our integration gives us
control over engine pinning, native packaging, diagnostics, and ABI qualification.
Existing LatticeDbSharp and CactusNeedleSharp work provides local experience
with native ownership, version manifests, and platform-specific verification.

## Decision

Create CedarSharp as an independent, reusable .NET binding for the official
Rust cedar-policy crate. Own a small Rust bridge exposing a versioned C ABI,
managed interop, public types, and native build/package verification. Preserve
upstream evaluation semantics; do not implement a second Cedar interpreter.

The initial API covers authorization, parsing and schema validation, entity and
request representation, determining policy IDs, complete evaluation diagnostics,
and engine/language/bridge version reporting. The initial public API is recorded in the README and source.

Hufu consumes CedarSharp only through a planned Penghou.Hufu.Cedar adapter.
Hufu's core and other consumers do not expose CedarSharp or native types.
Authority stores, grant issuance, approval workflows, revocation coordination,
budget accounting, and actual resource confinement remain outside CedarSharp.

Start with Windows x64 and .NET 8 / .NET 10. Native Linux x64 and macOS ARM64
qualification are part of the first distribution gate. NativeAOT and trimming require packaged consumer
evidence before being advertised. Keep a future CedarSharp.Analysis package and
its solver dependency separate from ordinary runtime evaluation.

## Consequences

We maintain the bridge, supported native binaries, upstream upgrade review,
interop behavior, and release conformance. Cedar remains the language/engine
owner. Engine assurances do not automatically prove our wrapper, entity mapping,
or host enforcement correct.

Upstream Cedar evaluation may return Allow with policy-error diagnostics.
CedarSharp preserves that distinction. Hufu additionally blocks on errors and
applies its own current-authority and execution checks.

No third-party wrapper implementation has been copied into this scaffold.
If future work reuses such code, preserve applicable licenses and attribution.

## Alternatives

- CedarDotNet remains a useful reference; it is not the selected dependency.
- A custom Hufu policy language/evaluator is deferred in favor of Cedar reuse.
- Mandatory remote authorization adds deployment and availability requirements;
  the first wrapper uses local evaluation.

## References

- [Official Cedar engine](https://github.com/cedar-policy/cedar)
- [Upstream FFI support](https://github.com/cedar-policy/cedar/tree/main/cedar-policy/src/ffi)
- [Cedar authorization and error semantics](https://docs.cedarpolicy.com/auth/authorization.html)
- [Hufu consumer ADR](../../../Penghou.Hufu/docs/decisions/0002-use-cedarsharp-for-policy-evaluation.md)
