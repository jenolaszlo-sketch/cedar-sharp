# CedarSharp contributor guidance

Read README.md, docs/implementation-handoff.md, docs/architecture.md,
docs/native-boundary.md, docs/verification.md, and ADR 0001 before implementation.
The repository has initial implementation. Consult docs/verification.md for
executed evidence; do not mistake planned CI gates for qualified packages.

Preserve upstream Cedar semantics and full diagnostics. Keep host policy,
Hufu grants, workflow state, and resource enforcement outside this library.
Never convert evaluation, serialization, native loading, or transport failures
into a successful authorization decision. An upstream Allow with policy errors
must remain distinguishable from an error-free Allow; Hufu decides its stricter
application behavior.

Pin the upstream source, Rust toolchain, dependency lockfile, and native ABI.
Validate ownership, UTF-8 boundaries, lengths, error conversion, concurrency,
and supported runtime identifiers. Do not expose raw pointers to public callers.
Keep experimental Cedar features and symbolic analysis out of the initial
runtime unless explicitly required and qualified.

Add meaningful native and packaged-consumer tests when behavior exists. Run
only supported platforms and report untested platforms honestly. Keep package
publication, remote repository creation, and Hufu integration changes separate
from implementing the wrapper unless the user's task authorizes them.
