# CedarSharp

[![NuGet](https://img.shields.io/nuget/v/CedarSharp)](https://www.nuget.org/packages/CedarSharp)
[![CI](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/workflows/ci.yml/badge.svg)](https://github.com/jenolaszlo-sketch/cedar-sharp/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/jenolaszlo-sketch/cedar-sharp)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

**Use Cedar policies in a .NET application without running a separate authorization service.** CedarSharp calls the official Rust [Cedar policy engine](https://github.com/cedar-policy/cedar) in process and gives C# code typed requests, decisions, and diagnostics. Put access rules in policies, supply the facts your application trusts, and ask whether a principal may perform an action on a resource.

It is useful when access rules span roles, resource ownership, or context and you want to change and validate those rules separately from application code. CedarSharp preserves Cedar's evaluation semantics and exposes errors so the application can make an explicit enforcement choice.

## Try it

Install the [stable package](https://www.nuget.org/packages/CedarSharp/1.0.0):

```sh
dotnet add package CedarSharp --version 1.0.0
```

Then evaluate a policy:

```csharp
using CedarSharp;

var policies = CedarPolicySet.FromPolicies(
    ("read-report", """permit(principal == User::"alice", action == Action::"read", resource == Document::"report");"""));

var request = new CedarAuthorizationRequest(
    new CedarEntityUid("User", "alice"),
    new CedarEntityUid("Action", "read"),
    new CedarEntityUid("Document", "report"),
    policies);

var result = new CedarEngine().Authorize(request);
Console.WriteLine(result.IsCleanAllow ? "Allowed" : "Denied or requires review");
```

`IsCleanAllow` means Cedar returned Allow with no policy errors. A completed evaluation (`IsSuccess`) can still return Deny or report policy errors. In a real application, authenticate the principal, resolve the resource and context from trusted data, and enforce the result at the resource boundary.

The [getting started guide](docs/getting-started.md) shows policy validation, diagnostics, entities, and schema-aware requests.

## What you get

- **Cedar behavior:** default deny, forbid precedence, and skip-on-error semantics from the pinned Cedar engine.
- **Reviewable decisions:** determining policy IDs, policy errors, structured diagnostics, and the original Cedar response.
- **Preflight checks:** policy syntax and strict schema validation, plus request, context, and entity checks.
- **Deployment choices:** framework-dependent, self-contained, trimmed, and NativeAOT applications on the [qualified environments](docs/deployment.md).

CedarSharp evaluates the policy and facts you provide. Your application owns authentication, trustworthy facts, policy rollout, auditing, and enforcement. Authorization does not automatically perform strict policy validation; validate policy bundles before deploying them.

## Compatibility

Version 1.0 is qualified by CI on these environments, with .NET 8 and .NET 10:

| Environment | Native asset |
| --- | --- |
| Windows Server 2025 x64 | `win-x64` |
| Ubuntu 24.04 x64 | `linux-x64` |
| macOS 15 ARM64 | `osx-arm64` |

The package includes the corresponding native assets. Older OS baselines and other architectures are not qualified. See [deployment](docs/deployment.md) for publishing and NativeAOT details, and the [verification record](docs/verification.md) for executed release evidence.

## Learn more

- [Getting started](docs/getting-started.md): validate policies and handle decisions safely.
- [Deployment](docs/deployment.md): supported targets, native assets, trimming, and NativeAOT.
- [API contract](docs/api-contract.md): result, exception, and compatibility behavior.
- [Documentation index](docs/README.md): architecture, native boundary, and contributor material.
- [Changelog](CHANGELOG.md) and [v1.0.0 release](https://github.com/jenolaszlo-sketch/cedar-sharp/releases/tag/v1.0.0).

## License and attribution

CedarSharp is an independent wrapper, unaffiliated with the Cedar project. The Cedar engine and policy language belong to the Cedar project and its contributors. CedarSharp and upstream Cedar use [Apache License 2.0](LICENSE); see [NOTICE](NOTICE) for attribution.
