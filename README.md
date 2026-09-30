# CedarSharp

[![NuGet](https://img.shields.io/nuget/v/CedarSharp)](https://www.nuget.org/packages/CedarSharp)
[![CI](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/workflows/ci.yml/badge.svg)](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/jenolaszlo-sketch/cedar-sharp)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

CedarSharp is an unofficial .NET 8 / .NET 10 wrapper around the Rust [Cedar policy engine](https://github.com/cedar-policy/cedar). It calls the original `cedar-policy` implementation through a small native bridge. The Cedar project and its contributors created and maintain Cedar; this repository provides the .NET wrapper and does not claim authorship of the original engine or policy language.

**[`1.0.0` is published on NuGet](https://www.nuget.org/packages/CedarSharp/1.0.0).**
Its [release workflow](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36727306968)
passed the full native, NativeAOT, package, and clean-consumer matrix. See the
[GitHub release](https://github.com/jenolaszlo-sketch/cedar-sharp/releases/tag/v1.0.0)
and [verification record](docs/verification.md) for release evidence.

Authorization, strict policy validation, policy/schema parsing,
request/context/entity checks, structured diagnostics, and loaded native
version discovery are implemented.

The 1.0 support scope is the tested CI environments: Windows Server 2025 x64,
Ubuntu 24.04 x64, and macOS 15 ARM64, with .NET 8 and .NET 10. These runner
versions are the deployment baselines; older OS and glibc versions are not
qualified. The [API contract](docs/api-contract.md) records result, exception,
nullability, and compatibility guarantees.

Packaging remains opt-in and requires verified native assets for every RID. Do not
infer native support from a successful managed build.

CedarSharp preserves Cedar's default deny, forbid precedence, and skip-on-error
semantics. An Allow with policy errors remains an Allow with policy errors.
Your application decides whether to reject that result. Hufu's adapter will own
its stricter decision handling, grants and authority lifecycle separately.

The library is trim- and NativeAOT-compatible. Typed convenience overloads that
serialize arbitrary CLR objects use reflection and are annotated
`RequiresUnreferencedCode`/`RequiresDynamicCode`; for AOT use the `JsonElement`
and `JsonNode` overloads (or `JsonTypeInfo<T>`), which take no reflection path.

## Usage

```csharp
using CedarSharp;

var engine = new CedarEngine();
var policies = CedarPolicySet.FromPolicies(new Dictionary<string, string>
{
    ["read-documents"] = "permit(principal, action == Action::\"read\", resource);"
});
var result = engine.Authorize(new CedarAuthorizationRequest(
    new CedarEntityUid("User", "alice"),
    new CedarEntityUid("Action", "read"),
    new CedarEntityUid("Document", "report"),
    policies));

// Parsing failures have no decision. Evaluation errors remain separately visible.
Console.WriteLine(result.Decision);
foreach (var error in result.PolicyErrors)
    Console.WriteLine($"{error.PolicyId}: {error.Error.Message}");
Console.WriteLine(engine.GetVersion());
```

A successful call (`IsSuccess`) means Cedar evaluated the request. It does not
mean access was allowed or evaluation was error-free. `Decision` is nullable:
Cedar parsing failures populate `Errors` and have no decision. Native loading,
ABI, output-decoding or transport failures throw `CedarBridgeException`.
Invalid CLR arguments or JSON syntax throw standard argument/JSON exceptions;
a structurally invalid Cedar call envelope throws `CedarInputException`.
No failure becomes an authorization grant or a fabricated Cedar denial.

```csharp
var schema = CedarSchema.FromText("""
    entity User;
    entity Document;
    action read appliesTo {
        principal: User, resource: Document, context: {}
    };
    """);
var validation = engine.ValidatePolicies(policies, schema);
Console.WriteLine(validation.IsValid);
```

`CheckPolicies` checks syntax; `ValidatePolicies` performs strict schema-based
policy validation. `CheckSchema`, `CheckRequest` (alias `CheckScope`),
`CheckContext`, and `CheckEntities` expose separate checks. `CheckRequest` checks
the three scope identifiers only — use `CheckFullRequest(request, schema)` (or the
principal/action/resource + context + entities overload) to validate scope,
context, and entities together. Authorization does not
implicitly validate policies. Supplying a schema to a request enables
schema-based entity/context parsing and, by default, request validation.
`validateRequest: false` disables only request validation.

### Fail-closed enforcement

```csharp
// Decision alone is not enough: an Allow can carry policy errors.
if (!result.IsCleanAllow)
    return Results.Forbid(); // or deny, log result.ToString(), audit result.Raw

result.RequireAllow();    // CedarAuthorizationException unless an error-free Allow
result.EnsureNoErrors();  // throws on any error/policy-error; accepts a clean Deny
validation.EnsureValid(); // CedarValidationException unless strict validation passed
```

`CedarBridgeException` is reserved for native loading, ABI, transport, or
response-decoding failure. Authorization and validation requirement failures
throw `CedarAuthorizationException` / `CedarValidationException`, each carrying
the originating result. No failure becomes a grant or a fabricated denial.

### Typed entities and context (no hand-written escapes)

```csharp
var alice = CedarEntityUid.Parse("User::\"alice\"");
var report = new CedarEntityUid("Document", "report");

var request = new CedarAuthorizationRequest(
    alice,
    new CedarEntityUid("Action", "read"),
    report,
    policies,
    context: new { trusted = true, ip = CedarValue.Ip("127.0.0.1") },
    entities: new[]
    {
        new CedarEntity(report, new { owner = CedarValue.Entity(alice) }),
    },
    schema: schema);

var full = engine.CheckFullRequest(request, schema);
var batch = engine.AuthorizeBatch(new[] { request }, parallel: true);

// Shorthands: policies from (id, text) tuples and a direct authorize overload.
var policies = CedarPolicySet.FromPolicies(("read", "permit(principal, action, resource);"));
var direct = engine.Authorize(alice, new CedarEntityUid("Action", "read"), report, policies);
```

`CedarPolicySet` and `CedarSchema` have value equality, so they can key caches
and memoized validation. Every call also emits an `Activity` on the
`CedarSharp` `ActivitySource` (`cedarsharp.authorize`,
`cedarsharp.validate_policies`) with decision, error-count, and engine/bridge
version tags for OpenTelemetry; see `CedarSharpDiagnostics`. Activities never
export principal/action/resource identities or error text by default; opt in with
`CedarSharpDiagnostics.IncludeIdentity` / `IncludeErrorDetails` for trusted sinks.

Cedar entity UIDs parse and format Cedar source grammar, not JSON:
`CedarEntityUid.Parse("Acme::User::\"a\\u{96ea}\"")` and
`uid.ToString()` produce `Type::"id"` with Cedar escapes. Empty ids are legal;
the type is validated as a Cedar identifier path.

### Deployment modes

The package supports framework-dependent, self-contained, RID-specific
publishing, trimming (`PublishTrimmed`), and NativeAOT (`PublishAot`). The loader
resolves the native asset relative to `AppContext.BaseDirectory`, so single-file
and AOT deployments should copy `runtimes/<rid>/native/` beside the app (the SDK
does this by default for RID publishes). `CEDARSHARP_NATIVE_PATH` selects a
self-built asset and is still hash- and identity-verified; relative paths resolve
against the current directory. The repository's `samples/CedarSharp.AotSmoke`
project is published and executed under NativeAOT for each supported RID in CI.
Single-file publishing has not been separately exercised by CI.

Inputs are immutable copied snapshots. `CedarPolicySet.FromJson` supports the
upstream `staticPolicies`, `templates`, and `templateLinks` representation;
`FromPolicies` assigns stable caller-controlled policy IDs. `CedarSchema.FromJson`
accepts Cedar JSON schemas. Context and entity JSON use Cedar's documented value
encoding, including entity and extension escapes. All results retain the full
upstream `Raw` JSON alongside typed diagnostics, related errors and source ranges
(UTF-8 byte offsets). Sets of determining IDs/errors have no ordering guarantee.

The NuGet package includes XML documentation for the public API.
Instances may be shared between threads. Calls are synchronous and parse inputs
per evaluation. The native library lives for the process lifetime; no disposal
is required. There are no cancellation or hard resource-deadline guarantees.
The bridge limits wire input to 16 MiB and output to 64 MiB; these are not limits
on Cedar's intermediate memory usage or execution time.
`CEDARSHARP_NATIVE_PATH` may point at a directory or file holding a self-built
native asset; it is still hash- and identity-verified before loading.

## Build and native CI

The baseline is Cedar **4.13.0**, Rust **1.94.0**, bridge **0.1.0**, ABI **1**.
Only `ipaddr`, `decimal`, and `datetime` extensions are enabled. Experimental
features and symbolic analysis are excluded. See [native contract](docs/native-boundary.md).

```powershell
# Requires the pinned Rust toolchain and an OS-native linker/SDK.
pwsh ./eng/Build-Native.ps1 -Rid win-x64
dotnet build CedarSharp.slnx -c Release
dotnet run --project tests/CedarSharp.Tests -c Release -f net8.0
dotnet run --project tests/CedarSharp.Tests -c Release -f net10.0
```

The CI matrix builds and executes on Windows x64, Linux x64, and macOS ARM64,
then assembles one NuGet archive and runs clean package consumers on each OS.
It also publishes and runs the NativeAOT smoke per RID. macOS uses a native ARM64
runner. Linux qualification targets the CI runner's glibc baseline; musl, Windows
ARM64 and macOS x64 are not supported by this slice. Oldest-OS baselines are not
yet qualified; see [docs/verification.md](docs/verification.md).

For a Windows-only local package smoke check after staging win-x64, use a
`-local` version and the explicit test override:

```powershell
dotnet pack src/CedarSharp/CedarSharp.csproj -c Release -o artifacts/local-pack `
  -p:CedarSharpEnablePack=true -p:CedarSharpLocalSmokePack=true -p:PackageVersion=0.1.0-local.1
./eng/Verify-NuGetPackage.ps1 -PackagePath artifacts/local-pack/CedarSharp.0.1.0-local.1.nupkg `
  -ExpectedRids win-x64 -ExpectedVersion 0.1.0-local.1
./eng/Test-PackagedConsumer.ps1 -PackagePath artifacts/local-pack/CedarSharp.0.1.0-local.1.nupkg `
  -Framework net8.0 -RuntimeIdentifier win-x64
```

The normal package gate requires all three verified RID assets. The local
smoke archive is an ignored development artifact and has not qualified Linux or
macOS.

## Publishing to NuGet

The separate [Publish to NuGet](.github/workflows/publish.yml) workflow is
started manually from `main`. It runs the complete native, package, and
six OS/framework consumer combinations at that commit, then verifies the exact
package again before publishing it through NuGet trusted publishing. A normal
push or CI run does not publish. The workflow publishes the version in
`src/CedarSharp/CedarSharp.csproj`; update that version before starting a new
release. NuGet will reject an already published version.

Trusted publishing was exercised for `0.1.0-preview.1` in
[publish run #1](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36648819227)
and for `0.2.0-preview.1` in
[publish run #2](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/runs/36664097224).
To release a new version, choose **Actions → Publish to NuGet → Run workflow**
on `main` after advancing the version and reviewing the
[release evidence](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/main/docs/verification.md).
The workflow requests a short-lived API key only after verification has passed.
See the [changelog](https://github.com/jenolaszlo-sketch/cedar-sharp/blob/main/CHANGELOG.md) and [NuGet trusted publishing setup](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

Native assets and their SHA-256 manifests must be deployed together. The loader
uses only an adjacent package asset or `runtimes/<rid>/native/`, verifies the
manifest and live version, and does not search arbitrary PATH locations or
download code. The hash detects corruption/mismatched staging; it does not
replace trust in the package publisher or secure deployment permissions.

See the [implementation plan](docs/implementation-handoff.md),
[verification evidence](docs/verification.md), [roadmap](ROADMAP.md), and
[external-consumer sample](samples/CedarSharp.Smoke).

## License and attribution

CedarSharp is licensed under [Apache License 2.0](LICENSE), the same license as
[upstream Cedar](https://github.com/cedar-policy/cedar/blob/main/LICENSE).
The Cedar engine and policy language are the work of the Cedar project and its
contributors. CedarSharp is an independent wrapper, unaffiliated with the Cedar
project and Penghou/Hufu. The [NOTICE](NOTICE) records upstream attribution;
native packages also carry the Cedar and dependency license and notice files.
Publication status is tracked in [verification evidence](docs/verification.md).
