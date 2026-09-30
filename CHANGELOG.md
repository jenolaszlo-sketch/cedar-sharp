# Changelog

## 1.0.0 — published 2026-09-30

- Freeze the public result, exception, and nullable API contract against the
  published `0.2.0-preview.1` binary baseline. The .NET SDK validates future
  packages during `dotnet pack`.
- Snapshot all batch request sequences before evaluation, including mutable
  `IReadOnlyList<T>` implementations.
- Exercise 4,096 deterministic adversarial native wire cases on each CI RID and
  check a shorter Linux run under Valgrind for definite leaks.
- Scope qualified deployment to the tested Windows Server 2025 x64, Ubuntu
  24.04 x64, and macOS 15 ARM64 CI environments on .NET 8 and .NET 10.

The full native, NativeAOT, package, and six-consumer matrix passed in
[CI run 36725177455](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36725177455).
The independent package verifier passed on the downloaded three-RID archive.
Valgrind found zero definite leaks; its possible-loss report is recorded in
[verification](docs/verification.md).
The [publication workflow](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36727306968)
passed at `68ffdaf`; the package, symbols, and
[GitHub release](https://github.com/jenolaszlo-sketch/cedar-sharp/releases/tag/v1.0.0)
are available.

## 0.2.0-preview.1 — published 2026-09-30

- Reject malformed native responses instead of interpreting missing fields as a
  clean authorization or validation result.
- Parse and format entity UIDs according to Cedar syntax, including empty IDs.
- Add `CedarAuthorizationException` and `CedarValidationException` for result
  requirements, keeping bridge failures distinct.
- Add typed context/entity helpers, full request checks, batching, copied input
  equality, and privacy-preserving diagnostic activities.
- Add source-generated JSON paths for trim and NativeAOT consumers; annotate
  reflection-based convenience overloads.
- Harden native asset overrides, package verification, SourceLink symbols and
  clean RID-published consumers.
- Qualify the three supported CI runner environments and NativeAOT smoke in
  [CI #5](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36659617333)
  at `a09d6f6`.

This is a preview API. Older OS baselines, the public API compatibility contract,
and adversarial native-boundary testing remain open for stable graduation.

## 0.1.0-preview.1 — published 2026-09-30

Initial Cedar 4.13.0 wrapper with authorization, strict validation, parsing,
structured diagnostics and verified native assets for Windows x64, Linux x64 and
macOS ARM64. Published through
[publish run #1](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36648819227).
