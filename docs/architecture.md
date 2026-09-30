# CedarSharp architecture

Current architecture for the 1.0 release. See [ADR 0001](decisions/0001-own-cedar-binding.md) for the binding decision and [verification](verification.md) for executed evidence.

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
reported policy errors. `IsCleanAllow` is the strict enforcement convenience
property; the full result retains diagnostics. Transport failure is never an
ordinary Cedar denial.

The wrapper preserves default deny, forbid precedence, and skip-on-error
behavior. Policy validation is explicit. Supplying a schema enables
schema-aware parsing and, by default, request validation; it does not implicitly
validate the policy bundle.

Validation results retain actionable errors and available source locations.
Policy syntax checking, strict schema validation, and request/entity checks
remain separate. Unknown required wire fields, incompatible bridge versions,
and unsupported operations produce explicit boundary failures.

## API scope

The bridge uses Cedar's JSON-oriented FFI with explicit UTF-8 buffers. Managed
results preserve the complete diagnostics payload and raw upstream JSON. The
API supplies typed request and entity helpers without rebuilding Cedar syntax
trees.

Calls serialize immutable request snapshots per evaluation. There is no
stateful preparsed policy/schema/entity handle. Any future cache needs an
explicit lifetime and versioning model.

Evaluation is synchronous and instances are thread-safe. The bridge does not
provide cancellation or hard resource deadlines.

The loader uses verified supported assets or an explicitly configured override
whose identity is validated and reported. It does not download or search for
arbitrary native libraries.

## Application boundary

The calling application supplies authenticated principals, resolved resources,
authentic relationships, context, and a versioned policy bundle. It decides
how to handle diagnostics, audit decisions, and enforce access. CedarSharp does
not issue grants, maintain revocation, or manage workflow state. Default
telemetry activities do not export principal/action/resource identities or
error text.

Authorization evaluates one concrete request. Policy implication and
equivalence analysis are outside the current runtime API.

## References

- [Cedar validation](https://docs.cedarpolicy.com/policies/validation.html)
- [Cedar authorization](https://docs.cedarpolicy.com/auth/authorization.html)

These links describe upstream Cedar behavior. The [API contract](api-contract.md)
describes what CedarSharp exposes.
