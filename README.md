# CedarSharp

CedarSharp is an unofficial .NET 8 / .NET 10 wrapper around the Rust [Cedar policy engine](https://github.com/cedar-policy/cedar). It calls the original `cedar-policy` implementation through a small native bridge. The Cedar project and its contributors created and maintain Cedar; this repository provides the .NET wrapper and does not claim authorship of the original engine or policy language.

**Status: Windows x64, Linux x64 and macOS ARM64 CI passed; package not published.** Authorization, strict policy
validation, policy/schema parsing, request/context/entity checks, structured
diagnostics, and loaded native version discovery are implemented. Platform and
verification evidence is tracked in [docs/verification.md](docs/verification.md).
Packaging remains opt-in and requires verified native assets for every RID. Do not
infer native support from a successful managed build.

CedarSharp preserves Cedar's default deny, forbid precedence, and skip-on-error
semantics. An Allow with policy errors remains an Allow with policy errors.
Your application decides whether to reject that result. Hufu's adapter will own
its stricter decision handling, grants and authority lifecycle separately.

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
policy validation. `CheckSchema`, `CheckRequest`, `CheckContext`, and
`CheckEntities` expose separate checks. `CheckRequest` checks the three scope
identifiers; check context and entities separately. Authorization does not
implicitly validate policies. Supplying a schema to a request enables
schema-based entity/context parsing and, by default, request validation.
`validateRequest: false` disables only request validation.

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
macOS uses a native ARM64 runner. Linux qualification targets the CI runner's
glibc baseline; musl, Windows ARM64 and macOS x64 are not supported by this slice.
NativeAOT, trimming, and single-file publishing are not yet qualified.

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
The wrapper has not been published as a package.
