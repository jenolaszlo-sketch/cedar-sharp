# CedarSharp graduation review and implementation handoff

> Historical review of the pre-`a09d6f6` tree. Findings F1-F8 were addressed
> in `a09d6f6`, and [CI #5](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36659617333)
> passed at that commit. See [current verification](verification.md) for release status.

Reviewed: 2026-09-30. Repository: `C:\Users\Laszlos\source\repos\CedarSharp`.
Base: `a95774ae764cc27560ce6418ce5f6579a53516a0` on `main`.

## Scope and starting state

The user requested a review of issues, design, usability, usefulness, and what is
needed to graduate the library, with a handoff for another model. This pass did
not fix implementation code, commit existing changes, or start publication.
The findings below are work to implement and verify.

The review includes **uncommitted changes**, not just the base commit:

- Modified: `README.md`, `CedarEngine.cs`, `CedarInputs.cs`, `CedarResults.cs`,
  `CedarSharp.csproj`, `JsonWire.cs`, `NativeBridge.cs`, and the managed test program.
- Untracked on arrival: `src/CedarSharp/CedarSharpDiagnostics.cs`.
- These changes add typed entities/values, UID parsing, result helpers, batching,
  combined checks, equality, tracing, native overrides, and managed symbols.
- Rust sources and workflows were unchanged relative to the base commit.

Preserve that work; do not reset it to obtain a clean checkout. Recheck
`git status` and the diff because the user may continue working. Source line
references below describe the reviewed tree. The existing
`docs/implementation-handoff.md` remains the original implementation plan.

**Assessment:** this is a useful preview foundation with real native and
packaged-consumer evidence. The API expansion is not ready to freeze as a stable
contract. Resolve the concrete findings, qualify the intended deployment baseline,
and establish compatibility/release evidence before calling it stable.
Extra RIDs and NativeAOT need not block a stable release if explicitly excluded
from its support contract.

## Constraints to preserve

- Read `AGENTS.md` and its referenced architecture, boundary, verification and ADR docs.
- Keep Apache-2.0 and clear independent-wrapper attribution. Cedar owns the engine
  and language; the wrapper must not claim their authorship.
- Preserve default deny, forbid precedence and Allow-with-policy-errors.
  Convenience helpers must not change the underlying decision or hide diagnostics.
- Parsing failure, validation findings, ordinary Deny and bridge failure remain
  distinct. Hufu authority, grants and enforcement belong in its adapter.
- Pin upstream source, Cargo.lock, features, Rust and ABI. A RID switch entry does
  not establish platform support.
- Preserve native output freeing on every path and process-lifetime library ownership.
- The user prefers Luna where practical. This handoff does not authorize new tasks
  or package publication.

## Evidence collected

| Check | Result and scope |
| --- | --- |
| Current tree, Release build | Passed, zero warnings/errors, .NET SDK 10.0.401. |
| Existing managed suite | 26/26 on .NET 8.0.31 and 26/26 on .NET 10.0.12, Windows x64. |
| Native Rust tests | 6/6 passed for `x86_64-pc-windows-msvc`; staging verification passed. |
| Native DLL used | SHA-256 `efa39f0413ca2e6eb6ad7884f033bbae6021d91a58be3aaac26cb86b35784b32`. |
| Current-tree local package | `artifacts/review-20260930/CedarSharp.0.1.0-local.review.1.nupkg` passed Windows-only verification; a matching `.snupkg` was produced. Ignored development artifacts. |
| Clean package consumers | Passed Windows x64 / net8.0 and net10.0, with isolated NuGet caches. |
| Targeted probes | Confirmed F1-F6 and F8 below using an external temporary consumer or the existing test executable. |
| Committed-source CI | [Run 36559189505](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36559189505) passed the three-RID / six-consumer gate for `a95774a`. It does not qualify the uncommitted API expansion. |
| Existing publishing attempt | [Run 36648819227](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36648819227) failed at NuGet login after all build, package, consumer and final archive checks passed. |

No Linux/macOS execution of the uncommitted tree, AOT/trimming/single-file test,
oldest-OS qualification, leak instrumentation or fuzzing campaign was performed.
Passing the current 26 tests does not invalidate the targeted findings.

### R1 — Publication currently lacks the required NuGet account input

The existing publishing run's public annotation was:

> Input required and not supplied: user

`publish.yml` passes `secrets.NUGET_USER` to `NuGet/login@v1`. Add that Actions
secret in the repository, or make an organization secret accessible, containing
the intended NuGet.org **profile name**. This proves a missing action input,
not whether the trust policy is otherwise correct. Do not guess the account name.

Policy fields remain owner `jenolaszlo-sketch`, repository `cedar-sharp`,
workflow filename `publish.yml`, environment empty. At the final review snapshot,
the public NuGet flat-container lookup for `cedarsharp` returned HTTP 404.
Recheck publication/indexing before deciding the next version; published package
versions are immutable. Do not rerun publishing automatically as part of a review.

## Concrete findings

P1 = fix first because a boundary contract can report false success.
P2 = correct before freezing the affected public API.

### F1 — P1: malformed validation responses can become valid

**Where:** `src/CedarSharp/CedarResults.cs:155-158`;
`src/CedarSharp/JsonWire.cs:59-62`. Regression in the uncommitted changes;
committed HEAD uses strict `JsonWire.Array` decoding for these fields.

`CedarValidationResult` reads `validationErrors` with `OptionalArray`, which
returns an empty array when the field is absent **or has the wrong JSON kind**.
The fixture `{"type":"success","validationErrors":42}` produced
`IsSuccess=True, IsValid=True`. Missing/null fields follow the same path.

The pinned upstream `ffi/validate.rs` success variant requires
`validationErrors`, `validationWarnings` and `otherWarnings` without omission
attributes. Treating malformed required data as no errors violates the wrapper's
response-contract failure policy.

This was reproduced through the internal decoder using reflection, not by
making pinned Cedar emit a malformed response. No valid-policy authorization
bypass through the shipped native engine was demonstrated.

**Fix:** strictly decode required arrays. Optional fields may be absent only
where upstream permits it; present fields must have the correct kind. Keep
contract exceptions wrapped as `CedarBridgeException` at the engine boundary.

**Acceptance:** missing/null/scalar/object error arrays, malformed elements and
unknown answer types fail decoding; valid empty arrays remain valid. Review
diagnostic and check-result fields against their pinned upstream schemas too.

### F2 — P2: UID parsing/formatting confuses JSON and Cedar text

**Where:** `src/CedarSharp/CedarInputs.cs:23-63`. Uncommitted addition.

`ToString()` uses `JsonSerializer.Serialize(Id)`. Interpolating its output into
`permit(principal == {uid}, action, resource);` failed Cedar parsing for IDs
containing `雪`, a quote, NUL or `<alice>`. For example it emits
`User::"\u96EA"`; Cedar Unicode escapes use braces, such as `\u{96ea}`.
JSON escaping belongs in the JSON wire format, not in Cedar source text.

`TryParse("User::\"\\u{96ea}\"", out _)` returned false. The unterminated
`User::"alice` and invalid type `Bad Type::"alice"` returned true.
String splitting plus JSON string parsing is not a Cedar UID parser.

**Fix:** use the pinned upstream parser/formatter through a small qualified
operation, or implement and differentially test its exact grammar. Put any
non-Cedar shorthand behind a separately named convenience API.

**Acceptance:** namespaces/type grammar, quotes, backslashes, Unicode,
supplementary scalars, NUL, Cedar escape forms, malformed and trailing input.
Verify output with Cedar itself, not just this wrapper's own round trip.

### F3 — P2: helpers confuse denial, operation errors and bridge failures

**Where:** `src/CedarSharp/CedarResults.cs:116-128,160-167`.
Uncommitted additions.

- An error-free default Deny has `IsCleanDeny=true`, but `EnsureNoErrors()`
  throws because it calls `RequireAllow()`.
- Ordinary Deny and invalid policies throw `CedarBridgeException`, whose public
  contract concerns native/ABI/transport failure and explicitly excludes Deny.
- Deny with policy errors produces the message "Authorization Allow had ...".

**Fix:** retain `IsCleanAllow` as caller opt-in to stricter enforcement.
Remove ambiguous helpers or separate their contracts: an error-only check accepts
a clean Deny; an allow requirement rejects it using a distinct domain result/
exception carrying the original result. Validation failures must not masquerade
as native transport failure.

**Acceptance:** clean Allow/Deny, Allow/Deny with errors, parse failure, validation
findings and real bridge failure. Assert exception types, retained data and
truthful messages, not only that something threw.

### F4 — P2: managed UIDs exclude legal empty Cedar IDs

**Where:** `src/CedarSharp/CedarInputs.cs:15-18`. Regression in the uncommitted
changes; HEAD rejects null IDs but permits empty ones.

The pinned engine accepts both `permit(principal == User::"", action, resource);`
and an entity JSON object with `"id":""`. However
`new CedarEntityUid("User", "")` throws, preventing strongly typed requests for
a valid upstream UID.

**Fix:** reject null IDs while permitting empty IDs. If intentionally choosing
a narrower contract, explicitly justify/document it; preserving Cedar semantics
favors allowing them. Update the existing test that expects empty-ID rejection.

**Acceptance:** empty IDs work across UID, entities, request and evaluation;
null remains rejected. Keep Hufu-specific ID restrictions in Hufu.

### F5 — P2: a bare relative native override finds the wrong manifest

**Where:** `src/CedarSharp/NativeBridge.cs:49-56,101-116`.
Uncommitted override feature.

With working directory `native/staging/win-x64/native` and
`CEDARSHARP_NATIVE_PATH=cedarsharp_native.dll`, the library is found but the
manifest is sought in the application's base directory. The probe failed even
though the correct manifest was beside the selected library. An absolute
override succeeds.

**Fix:** normalize one absolute asset path before deriving its manifest path,
hashing or loading. Define relative-path support; otherwise reject it explicitly.

**Acceptance:** absolute/relative file overrides, flat/nested directory overrides,
missing/tampered assets, and current directory different from application base.
Use fresh subprocesses for loader tests.

### F6 — P2: loader/package tests inherit the native override

**Where:** `tests/CedarSharp.Tests/Program.cs:231-244`;
`eng/Test-PackagedConsumer.ps1`. Exposed by the new override feature.

With a valid `CEDARSHARP_NATIVE_PATH` in the parent environment, the managed
suite passed 25/26. The negative subprocess inherited it and loaded the good
staged library instead of its intentionally broken copy. This is test isolation,
not a bypass of manifest verification.

Packaged consumers also inherit that variable. Current clean GitHub runners do
not set it, but local smoke can unknowingly exercise an external library.

**Fix:** explicitly clear the override in negative probes and package consumers;
test overrides in their own controlled cases. Assert the loaded path belongs
to the isolated consumer's deployment/package as appropriate.

**Acceptance:** test with valid, invalid and absent parent overrides; package
smoke must establish which native asset it loaded.

### F7 — P2: symbols are generated but not delivered to publication

**Where:** `src/CedarSharp/CedarSharp.csproj:15-18`,
`.github/workflows/ci.yml:94-99`, `.github/workflows/publish.yml:45-75`.
Introduced by uncommitted symbol settings.

Local pack produced both `.nupkg` and `.snupkg`. CI uploads only
`CedarSharp.*.nupkg`, leaving the publish job without the symbols archive.

**Fix:** retain and verify symbols as workflow artifacts and publish them with
the exact tested package. Verify SourceLink/source availability at the released
commit. These are managed symbols; native debug symbols are a separate decision.

**Acceptance:** matching version, portable PDB/source mappings for both TFMs,
and symbol publication without repacking the tested binaries.

### F8 — P2: typed-input contracts need negative cases

**Where:** `CedarInputs.cs:81-93,112-126,233-262` and
`JsonWire.cs:28-35`. Uncommitted additions.

Confirmed examples:

- `FromPolicies` claims later duplicate IDs overwrite earlier ones. It emits
  duplicate JSON keys, which Cedar rejects with `CedarInputException`. Prefer
  an early duplicate-ID argument error to silent policy replacement.
- Null parents are accepted by `CedarEntity`, then cause `NullReferenceException`
  during serialization. Null entity elements have the same unchecked dereference.
- `CedarPolicySet.FromJson("{\"templateLinks\":[1]}").ToString()` throws
  `InvalidOperationException` through `PolicyIds`. Formatting a raw snapshot
  should not crash before its intended Cedar validation.
- `CedarValue.FromObject(new { name = "\ud800" })` silently becomes
  `{"name":"\uFFFD"}`, unlike strict string/UID inputs. UTF-8 validation after
  serialization cannot detect this change to caller data.

**Fix:** align validation across string, JsonElement, JsonNode and object inputs.
Reject null collection elements early, make diagnostic formatting total, and
document raw-snapshot versus validated-value semantics. Preserve scalar data
without silent replacement or explicitly narrow the object conversion contract.
Do not rebuild Cedar's whole type checker in .NET.

**Acceptance:** duplicate/null IDs/values, null parents/entities, malformed raw
shapes, disposed elements, invalid Unicode, deep/oversized values, and equivalent
behavior across supported overloads.

## Design and usability opportunities

These are prioritized opportunities, not requirements that every feature ship in 1.0.

1. **Enforce public invariants.** Derive `CedarFullRequestCheckResult.IsSuccess`
   from three non-null results: its positional constructor permits contradictory
   success today. Defensively own public collection inputs. Make publicly
   constructible `CedarVersion.ToString()` safe for short/null hashes rather than
   unconditionally slicing `Sha256[..12]`.
2. **Keep a small, composable object model.** Separate input construction, engine
   calls, response decoding and transport. Add an internal decoder/transport seam
   for malformed-response tests. Consider a narrow `ICedarAuthorizer` only if
   actual consumer tests need substitution, and design test-result creation with
   it. Avoid inheritance hierarchies or public native handles for mockability.
3. **Finish useful typed data.** Consider reusable immutable entity-set/context
   snapshots and typed template/link builders based on real consumer needs.
   Typed entities currently omit entity tags; verify pinned upstream support
   before adding them. Retain raw JSON. Document signed 64-bit numbers, sets,
   entity references and decimal/ip/datetime/duration extensions; arbitrary CLR
   serialization is not equivalent to valid Cedar data.
4. **Allow caller-owned serialization.** Provide `JsonTypeInfo<T>` /
   `JsonSerializerContext` overloads; consider copied/frozen serializer options.
   The current reflection serializer limits AOT and does not accept caller
   naming/converter configuration. Keep JSON-element paths usable without
   reflection. Qualify trimming/AOT separately.
5. **Specify telemetry privacy and outcomes.** Subscribing currently exports
   complete principal/action/resource UIDs automatically; the probe saw all three.
   Prefer counts/types/outcome/version by default and opt-in identity/error-text
   tags. Distinguish parse failure, ordinary Deny, evaluation diagnostics and
   transport failure. `cedar.bridge.version` currently uses managed assembly
   version, which can diverge from the native bridge on later releases.
6. **Bound batching.** `parallel: true` uses `Parallel.For` without a caller
   concurrency limit and changes exception shape to AggregateException. Specify
   fail-fast versus per-item failure, ordering, input snapshots and concurrency.
   Cancellation can stop scheduling but cannot interrupt a native call. Do not
   describe a managed loop as a native batch-performance optimization.
7. **Measure before caching/handles.** Benchmark small/large policy/entity bundles,
   repeated calls, diagnostics-heavy results and concurrent load. Separate
   serialization, parsing/evaluation, allocations and latency. Candidates include
   the per-call singleton lock, repeated JSON copies and retained raw-text copies.
   Equality is exact serialized-text equality, not semantic policy equivalence or
   canonical JSON. Never use it as an authorization-equivalence proof.
8. **Explain full-check scope.** `CheckFullRequest` makes three native calls and
   reparses the schema. It does not validate policies or confirm every referenced
   entity exists. Its request overload accepts a schema different from
   `request.Schema`; document that or add an overload using the request's schema.
9. **Align self-build claims with tools.** The loader recognizes win-arm64,
   linux-arm64 and osx-x64, but build/test/verifier scripts accept only the three
   qualified RIDs. The suggested script cannot build those extra assets. Supply
   an explicit experimental recipe or full tooling/qualification; do not imply
   they are package-supported RIDs.

## Work packages and graduation gates

### G1 — Correctness and API stabilization

- Preserve the current diff, fix F1-F8, and decide which conveniences deserve
  long-term support.
- Add regressions at observable boundaries, especially native-vs-managed UID
  grammar and malformed result decoding. Do not mirror implementation details.
- Split growing files by cohesive public types where useful; keep immutable
  inputs/results and a small facade.
- Establish a public API compatibility baseline and preview/stable versioning.
  Freeze exception semantics, nullability, equality and thread-safety/lifetime
  contracts before 1.0.
- Run full three-RID CI at the resulting committed SHA. Historical CI is not
  evidence for the uncommitted additions.

### G2 — Deployment, ownership and package qualification

- Choose oldest supported Windows, macOS and Linux/glibc baselines. Inspect native
  dependencies/imported versions and execute on those baselines. Success on
  Ubuntu 24.04 / macOS 15 / Windows 2025 hosted runners does not imply all
  .NET-supported operating systems.
- Add actual `dotnet publish -r <rid>` consumers, framework-dependent and
  self-contained if both are supported. Current smoke restores/runs without an
  explicit RID publish. Verify native asset and adjacent manifest deployment.
- Keep NativeAOT, trimming and single-file unsupported until tested. These are
  optional scope, not reasons to block all preview usage.
- Add measured memory/ownership stress and native boundary fuzzing over valid
  allocated buffers with arbitrary bytes/operation IDs. Do not fuzz dangling
  pointers or double-free, which violate the ABI's caller preconditions.
- Harden archive verification: remove `Select-Object -First 1` before the nuspec
  count check (`Verify-NuGetPackage.ps1:21` cannot detect a second nuspec).
  Compare dependency inventories to the resolved locked graph, not just their
  own listed records. Verify package metadata, provenance, symbols and notices.
- Describe hashes accurately: asset/manifest agreement is not independent
  publisher authenticity. Consider exposing upstream/lock/source identities in
  version diagnostics for application audits.

### G3 — Repeatable release and maintenance

- Resolve R1 and F7; retain manual release and promotion of the exact tested
  artifact. Keep OIDC login immediately before upload and permissions confined
  to the publishing job.
- Record wrapper version/commit, upstream commit, toolchain/lock, package checksums,
  CI run and supported platform matrix. Add changelog/release notes and tags for
  tested release commits. Never overwrite a published version to repair it.
- Reduce duplicated RID/version baselines across C#, Rust scripts, verifiers and
  samples through a reviewed build-time source or consistency check. Do not
  replace pinned trust values with unchecked environment configuration.
- Set a .NET SDK/roll-forward policy (`global.json` is absent). Floating SDK setup
  and `LangVersion=latest` reduce repeatability.
- Add scoped formatting/lint and dependency-advisory checks, plus an upstream
  Cedar upgrade procedure requiring semantic/native/package requalification.
- Refresh roadmap, verification ledger, ADR status and loader docs. They still
  describe 17 tests, older commits, no override/Lazy failure caching and a release
  process not yet established. Distinguish configured, attempted, published and
  qualified states.

### G4 — Prove usefulness with real consumers

- Provide a complete schema + named policies + hierarchy + context example with
  Deny, parse failure and Allow-with-errors handling.
- Add an application/ASP.NET example with singleton engine and explicit policy
  validation; keep framework dependencies out of the core package and Hufu's
  adapter in Hufu.
- Demonstrate template linking, typed entities, redacted telemetry, loaded
  identity diagnostics and a clean NuGet install/run.
- Benchmark before parsed handles. If justified, require SafeHandle ownership,
  disposal-race tests and coherent immutable input versions. Keep SymCC separate.

## Commands and reproduction seeds

The native review used the ignored `.tools/Set-LocalToolchain.ps1` for the Windows
linker/Rust environment. It is not a portable bootstrap contract; other machines
need pinned Rust and their native OS linker/SDK.

```powershell
dotnet build CedarSharp.slnx -c Release
dotnet run --project tests/CedarSharp.Tests -c Release -f net8.0
dotnet run --project tests/CedarSharp.Tests -c Release -f net10.0
pwsh ./eng/Test-Native.ps1 -RuntimeIdentifier win-x64
pwsh ./eng/Verify-NativeStaging.ps1 -RuntimeIdentifier win-x64

# Windows-only local archive; never publish this development package.
dotnet pack src/CedarSharp/CedarSharp.csproj -c Release -o artifacts/review-local `
  -p:CedarSharpEnablePack=true -p:CedarSharpLocalSmokePack=true `
  -p:PackageVersion=0.1.0-local.review.2
pwsh ./eng/Verify-NuGetPackage.ps1 `
  -PackagePath artifacts/review-local/CedarSharp.0.1.0-local.review.2.nupkg `
  -ExpectedRids win-x64 -ExpectedVersion 0.1.0-local.review.2
pwsh ./eng/Test-PackagedConsumer.ps1 `
  -PackagePath artifacts/review-local/CedarSharp.0.1.0-local.review.2.nupkg `
  -Framework net8.0 -RuntimeIdentifier win-x64
# Repeat the consumer on net10.0; CI supplies Linux/macOS evidence.
```

```csharp
var engine = new CedarEngine();
var uid = new CedarEntityUid("User", "雪");
var check = engine.CheckPolicies(CedarPolicySet.FromText(
    $"permit(principal == {uid}, action, resource);"));
// Current: check.IsSuccess == false.

CedarEntityUid.TryParse("User::\"alice", out var malformed);
// Current: true for an unterminated quoted identifier.

var deny = engine.Authorize(new CedarAuthorizationRequest(
    new("User", "alice"), new("Action", "read"), new("Document", "report"),
    CedarPolicySet.Empty));
deny.EnsureNoErrors();
// Current: CedarBridgeException despite an error-free Deny.
```

For F1, inject `{"type":"success","validationErrors":42}` into the internal
`CedarValidationResult(JsonElement)` constructor. The review used reflection;
durable tests should use an internal test seam. Expected after fixing: decoding
fails rather than reporting valid. The temporary review harness is not a durable
repository dependency; use these reproductions and expected outcomes.

## Primary references

- [Pinned validation FFI](https://github.com/cedar-policy/cedar/blob/324d3c09fc94ab91464ec340eb81e4b167deb6f5/cedar-policy/src/ffi/validate.rs):
  required response fields, inspected in the matching local 4.13.0 crate.
- [Cedar grammar](https://docs.cedarpolicy.com/policies/syntax-grammar.html):
  entity/string syntax; behavior also confirmed against pinned Cedar.
- [Entities/context JSON](https://docs.cedarpolicy.com/auth/entities-syntax.html):
  JSON escapes and typed value contracts.
- [Cedar validation](https://docs.cedarpolicy.com/policies/validation.html):
  policy/request checks and entity-existence limits.
- [.NET JSON source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation):
  reflection and NativeAOT implications.
- [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing):
  account input and repository/workflow trust setup.

Start with G1 correctness/API work while keeping R1 visible, then G2/G3 evidence.
Select G4 additions from real consumer needs; a larger API does not itself make
the wrapper ready for stable release.
