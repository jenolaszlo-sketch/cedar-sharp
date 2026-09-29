# Native ABI and distribution contract

Baseline: Cedar 4.13.0, bridge 0.1.0, ABI 1, Rust 1.94.0.
Cedar crate checksum: `5e4f5e46c425491b7133eecb3030d82d27301b84bfa08533c59a6902cca7eb80`.
Source commit: `324d3c09fc94ab91464ec340eb81e4b167deb6f5` (published crate VCS metadata).
Only `ipaddr`, `decimal`, `datetime` features are enabled; Cargo.lock is committed.

## ABI 1

```c
typedef struct { uint8_t *data; size_t len; } CedarSharpBuffer;
uint32_t cedarsharp_abi_version(void);
uint32_t cedarsharp_call_v1(uint32_t operation, const uint8_t *input,
                           size_t input_len, CedarSharpBuffer *output);
void cedarsharp_free_v1(CedarSharpBuffer buffer);
```

C calling convention; pointer-sized lengths; sequential two-field output layout.
The caller owns readable input for the call and a writable output struct. Null
input is valid only for length zero. Empty input has no valid Cedar JSON request;
version discovery uses `{}`. Embedded NUL is an ordinary byte, never a terminator;
JSON syntax still applies. UTF-8 must be valid. Input limit 16 MiB, output 64 MiB.
The limits bound wire messages, not intermediate allocations or evaluation time.

The bridge initializes output before processing. Nonempty output is one owned
Rust boxed byte slice with no terminator; the caller frees it exactly once using
the originating library and the unmodified pointer/length. A null/zero buffer
is safe to free. Foreign pointers, double-free, unreadable input and invalid
output addresses violate the C contract and are not recoverable validation errors.

| Operation | Upstream operation |
| --- | --- |
| 0 | Loaded versions and build identity |
| 1 | is_authorized |
| 2 | validate (strict) |
| 3 | check_parse_policy_set |
| 4 | check_parse_schema |
| 5 | check_parse_entities |
| 6 | check_parse_context |
| 7 | check_parse_scope_variables |

Cedar operation payloads follow the exact pinned upstream JSON FFI contract.
No stateful/thread-local Cedar preparsing cache or experimental API is exposed.
Unknown request fields are rejected; arbitrary entity/context attribute names
remain valid input data. Duplicate keys are rejected where upstream rejects them.

Status 0 means a complete Cedar answer, including Cedar's `failure` answer;
authorization Deny is not a boundary failure. Status 1 is invalid boundary input,
2 unsupported operation, 3 caught panic, and 4 oversized output. Boundary errors
use a JSON object with `message`. Recoverable Rust unwinding is caught. Abort,
OOM abort, stack overflow and memory faults are not promised recoverable.

Managed calls copy the output and free it in `finally`, including JSON decoding
failure. A Lazy singleton verifies and retains the native library for process
lifetime, so concurrent calls cannot race library unloading. Public APIs expose
no raw pointers and require no disposal.

## Asset identity and distribution

| RID | Target | Native CI |
| --- | --- | --- |
| win-x64 | x86_64-pc-windows-msvc | Windows x64 |
| linux-x64 | x86_64-unknown-linux-gnu | Linux x64/glibc |
| osx-arm64 | aarch64-apple-darwin | macOS ARM64 |

These are qualification targets; see verification.md for executed evidence.
No cross-platform claim follows from managed compilation. No musl, osx-x64 or
Windows ARM64 assets are selected. OS/libc minimums must be confirmed by CI and
recorded before distribution; the runner baseline is not automatically a
compatibility promise for older operating systems.

Build with Cargo --locked and explicit targets. Each staged asset carries
`cedarsharp-native.json`: ABI/engine/bridge/toolchain, target/RID, binary SHA-256,
source commit, lock/source hashes and features. Packages carry upstream and
transitive dependency license notices. Cedar 4.13.0's published crates omit
the workspace license text, so staging includes the exact pinned repository
LICENSE and NOTICE. `defmt-parser` omits its license file and uses its declared
Apache-2.0 option from that standard license text. The loader checks hash and expected
identity, then queries live versions/features before authorization. It loads
only the assembly-adjacent native asset or its RID-specific runtimes directory.
There is no automatic download, global search, or implicit override.

Packaging is disabled by default. A local smoke override can pack only the staged Windows asset with a local prerelease version. The normal opt-in build requires all three manifests;
CI additionally verifies actual package content and clean consumers on all
three native platforms and .NET 8/10. A manifest alone does not constitute
qualification. Publication requires those gates and a separate release action.

Re-run diagnostics, native safety, concurrency, package and platform gates on
upgrades. Record old identity so consumers can audit historical decisions.
Hufu determines when new semantics can apply to admitted policy bundles.
