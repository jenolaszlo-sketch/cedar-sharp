# Changelog

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
