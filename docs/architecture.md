# CedarSharp architecture

Status: Initial implementation of [ADR 0001](decisions/0001-own-cedar-binding.md).

## Layers

The managed library calls our native bridge in process. The bridge delegates to
the pinned official Cedar engine. No network service is required for evaluation.

| Layer | Owns |
| --- | --- |
| Managed API | Typed requests/results, validated marshalling, deterministic lifetime, version discovery |
| Interop | ABI declarations, loader, byte buffers, native result ownership |
| Rust bridge | C-compatible exports, safe input decoding, structured boundary errors, delegation to Cedar |
| Official Cedar | Policy parsing, validation, evaluation, language semantics |
| Application | Authentic input facts, policy lifecycle, error response, resource enforcement |

## Semantics and errors

Keep successful evaluation, policy diagnostics, and bridge failure separate.
An evaluation result contains Cedar's decision, determining policy IDs, and all
reported policy errors. A boolean convenience API must not erase diagnostics or
pretend transport failure is an ordinary Cedar denial. Initially prefer the
full result API.

Preserve default-deny, forbid precedence, and skip-on-error behavior exactly.
Do not silently make schema validation mandatory inside upstream-equivalent
authorization; expose validation explicitly. Applications such as Hufu will
require validation and schema-compliant requests before evaluating authority.

Validation results retain actionable errors and available source locations.
Do not conflate parsing a policy with validating it against a schema. Keep
request/entity validation and policy validation distinct where upstream does.
Unknown required wire fields, incompatible bridge versions, and unsupported
operations produce explicit boundary failures.

## API scope

Begin with the upstream JSON-oriented FFI surface where suitable. Use explicit
UTF-8 buffers and typed results, preserving the complete diagnostics payload.
Avoid inventing a broad managed entity DSL or rebuilding Cedar syntax trees
before the first complete authorization path works.

The prototype may use per-call serialized input. Measure realistic repeated
evaluation before deciding whether immutable parsed policy/schema/entity handles
are necessary. If introduced, own them through SafeHandle or an equivalent
proved lifetime strategy; pin snapshots to prevent mixed policy versions.

Expose synchronous evaluation honestly. Adding Task.Run wrappers does not make
native work cancellable. Document thread safety, and do not claim cancellation
or hard resource deadlines without an enforceable implementation.

No implicit runtime downloads or fallback to arbitrary native libraries on the
authorization path. Load only verified supported assets or an explicitly
configured override whose identity is validated and reported.

## Hufu mapping

The Hufu adapter supplies tenant-scoped subjects, resolved resources, authentic
hierarchies, normalized actions, context, and an exact versioned policy bundle.
CedarSharp does not resolve filesystem paths, discover trust, issue grants,
maintain revocation, or decide whether a workflow is active.

Hufu blocks on policy diagnostics, native failure, unknown authority, or stale
execution state. CedarSharp retains the underlying engine response for precise
audit and debugging. No secret material should be copied into default logs.

## Analysis boundary

Ordinary authorization answers one concrete request. Proving that every request
allowed by one policy is allowed by another requires separate analysis and
explicit schema/subject assumptions. Cedar's SymCC offers implication and
equivalence checks, but it is not part of the initial bridge.

Future analysis must expose unsupported features, solver timeout/unknown, and
counterexamples. No successful sample of ordinary authorization calls constitutes
a containment proof. Keep solver installation, process limits, and lifecycle
separate from the runtime package.

## References

- [Cedar validation](https://docs.cedarpolicy.com/policies/validation.html)
- [Cedar authorization](https://docs.cedarpolicy.com/auth/authorization.html)
- [Symbolic compiler](https://github.com/cedar-policy/cedar/tree/main/cedar-policy-symcc)

These links describe upstream capabilities, not features implemented by this scaffold.
