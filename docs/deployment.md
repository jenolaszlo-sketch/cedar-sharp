# Deployment

CedarSharp is a managed wrapper around an in-process native Cedar bridge. The NuGet package contains verified native assets for the three qualified runtime identifiers (RIDs).

| Tested CI environment | RID | Frameworks |
| --- | --- | --- |
| Windows Server 2025 x64 | `win-x64` | .NET 8, .NET 10 |
| Ubuntu 24.04 x64 | `linux-x64` | .NET 8, .NET 10 |
| macOS 15 ARM64 | `osx-arm64` | .NET 8, .NET 10 |

These exact runner environments are the 1.0 deployment baselines. Older OS or glibc versions, Linux musl, Windows ARM64, and macOS x64 are not qualified. The [verification record](verification.md) links the executed CI, package-consumer, and NativeAOT runs.

## Publish an application

Add the [CedarSharp package](https://www.nuget.org/packages/CedarSharp/1.0.0), then publish for a qualified RID:

```sh
dotnet publish -c Release -r win-x64 --self-contained false
```

Choose the RID that matches the destination. Framework-dependent and self-contained RID publishes are supported, as are trimming and NativeAOT. The [NativeAOT sample](../samples/CedarSharp.AotSmoke) is built and run for each qualified RID in CI.

The native library and its `cedarsharp-native.json` manifest must travel together. The SDK normally copies package assets for a RID publish. The loader looks beside the application and under `runtimes/<rid>/native/` relative to `AppContext.BaseDirectory`. It verifies the binary hash, declared identity, and loaded version before use. It does not download a library or search arbitrary system paths.

Single-file publishing has not been separately qualified. If using it, verify that the native library and manifest are present at a location the loader checks and test the exact deployment yourself.

## Trimming and NativeAOT

Use the `JsonElement` / `JsonNode` APIs or source-generated `JsonTypeInfo<T>` for context and attributes. Convenience overloads that serialize arbitrary CLR objects use reflection and carry `RequiresUnreferencedCode` and `RequiresDynamicCode` annotations. The [getting started guide](getting-started.md) and [NativeAOT sample](../samples/CedarSharp.AotSmoke/Program.cs) show both paths.

## Native override

`CEDARSHARP_NATIVE_PATH` may select a self-built native file or a directory containing it. Relative paths resolve against the current directory. The matching manifest remains required, and the loader still verifies hash and identity. Use this only when you control and trust the deployed asset and its manifest.

## Runtime behavior

Requests and policy inputs are copied snapshots. `CedarEngine` instances can be shared across threads; calls are synchronous, and the native library stays loaded for the process lifetime. There is no disposal requirement, cancellation guarantee, or hard execution deadline. The bridge limits input wire messages to 16 MiB and output wire messages to 64 MiB; these limits do not bound Cedar's intermediate memory use or execution time.

For ABI and packaging details, see the [native boundary](native-boundary.md). For failures and decision semantics, see the [API contract](api-contract.md).
