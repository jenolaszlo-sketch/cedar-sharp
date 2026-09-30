# CedarSharp 1.0 API contract

The published `0.2.0-preview.1` package was the binary compatibility baseline
for the 1.0 release candidate. The published `1.0.0` package is now the baseline
for future builds. SDK package validation runs during every normal `dotnet pack`
for both `net8.0` and `net10.0`. Changes to public signatures, target frameworks,
nullable annotations, or the behavior below require an explicit review and a
versioning decision. Package validation catches binary breaks; the integration
suite and this contract cover behavior it cannot infer.

| Contract | Behavior |
| --- | --- |
| `CedarAuthorizationResult.IsSuccess` | Cedar completed evaluation; it does not mean Allow. |
| `Decision` | Nullable; absent on a failed authorization envelope. |
| `PolicyErrors` | Present separately from `Errors`, including on an Allow. |
| `RequireAllow()` | Returns only for an error-free Allow; otherwise throws `CedarAuthorizationException` with the originating result. |
| `EnsureNoErrors()` | Accepts a clean Deny; throws `CedarAuthorizationException` for operation or policy errors. |
| `CedarValidationResult.EnsureValid()` | Throws `CedarValidationException` with the originating result unless strict validation completed without findings. |
| `CedarBridgeException` | Native load, ABI, transport, or response-decoding failure; never a Cedar decision. |
| `CedarInputException` | Cedar call envelope rejected before evaluation; derives from `ArgumentException`. |
| CLR input and JSON errors | Null required objects produce `ArgumentNullException`; invalid arguments produce `ArgumentException`; malformed JSON produces `JsonException`. |
| Results and diagnostics | Collections are non-null, including for failed operations. `Raw` is a copied upstream JSON value. |
| Batch input | The request sequence is copied before evaluation; results preserve input order in sequential and parallel modes. |

`CedarAuthorizationRequest.Schema` is nullable; `Policies` is not. The
`CedarAuthorizationResult.Errors` collection and exception `Result` properties
are non-null. Optional diagnostic fields such as `CedarDiagnostic.Help` are
nullable. The integration suite checks these representative annotations and
the result/error behavior above.

The native bridge is process-lifetime state. Native calls are synchronous and
cannot be interrupted once started. Wire input and output are capped at 16 MiB
and 64 MiB respectively; Cedar's intermediate memory and evaluation time are
not bounded by those caps. Single-file publishing and older operating systems
remain outside the qualified deployment set.
