# Behavioral tests

The dependency-free `CedarSharp.Tests` executable is an integration assertion
suite, not a `dotnet test` adapter project. Build native staging first, then run:

```powershell
dotnet run --project tests/CedarSharp.Tests -c Release -f net8.0
dotnet run --project tests/CedarSharp.Tests -c Release -f net10.0
```

It checks decisions, forbid/hierarchy semantics, Allow-with-errors, structured
diagnostics, templates, parsing vs validation, schema/data checks, Unicode,
invalid inputs, repeated/concurrent calls and loader failure probes in clean
subprocesses. It exits nonzero on failure.

Native Rust unit tests compare the C bridge with direct upstream Rust FFI calls
from the same dependency graph, plus pointer/length/UTF-8/error checks. Fixtures
are authored for CedarSharp; upstream implementation code is not copied.

CI separately packs the actual NuGet archive and runs the external consumer on
each RID/framework. See [verification](../docs/verification.md) for executed
results and remaining platform/ownership evidence.
