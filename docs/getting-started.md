# Getting started

CedarSharp lets a .NET application ask the official Cedar engine for a decision about a principal, action, and resource. A policy is the rule; the request supplies the facts. The application still authenticates callers, obtains trustworthy facts, and enforces the decision.

Install `CedarSharp` from [NuGet](https://www.nuget.org/packages/CedarSharp/1.0.0):

```sh
dotnet add package CedarSharp --version 1.0.0
```

## Make a decision

```csharp
using CedarSharp;

var engine = new CedarEngine();
var policies = CedarPolicySet.FromPolicies(
    ("read-report", """permit(principal == User::"alice", action == Action::"read", resource == Document::"report");"""));

var request = new CedarAuthorizationRequest(
    new CedarEntityUid("User", "alice"),
    new CedarEntityUid("Action", "read"),
    new CedarEntityUid("Document", "report"),
    policies);

var result = engine.Authorize(request);
if (!result.IsCleanAllow)
{
    // Deny access; record the diagnostics in an appropriate audit sink.
    Console.WriteLine(result);
}
else
{
    Console.WriteLine("Allow access");
}
```

Use stable, caller-chosen policy IDs so diagnostics point back to your policy bundle. Cedar defaults to Deny when no policy permits the request; an applicable `forbid` overrides a `permit`. Cedar can also return Allow with policy evaluation errors. `IsCleanAllow` requires an error-free Allow. `IsSuccess` only says the authorization call completed.

For a throwing enforcement style, call `result.RequireAllow()`. A clean Deny and any result with errors throw `CedarAuthorizationException`, which carries the result.

## Validate a policy bundle before rollout

A schema gives Cedar the entity types, actions, and context expected by your application. Strict validation catches policies that do not match that model:

```csharp
var schema = CedarSchema.FromText("""
    entity User;
    entity Document;
    action read appliesTo {
        principal: User, resource: Document, context: {}
    };
    """);

var validation = engine.ValidatePolicies(policies, schema);
validation.EnsureValid();
```

`CheckPolicies` checks syntax; `ValidatePolicies` performs strict schema-based validation. Authorization does not validate the policy bundle implicitly. Validate the exact bundle you intend to deploy, and decide how to handle validation warnings in your own rollout process.

Supplying `schema: schema` to a `CedarAuthorizationRequest` enables schema-aware context and entity parsing and, by default, request validation. `engine.CheckFullRequest(request, schema)` checks scope, context, and entities together before authorization. `CheckRequest` checks only the principal, action, and resource.

## Supply context and entities

Context holds request-specific facts. Entities hold resource attributes and relationships. For example, a policy can inspect `context.trusted` and the resource's `owner`. Construct these facts from data your application trusts:

```csharp
using System.Text.Json;

var alice = new CedarEntityUid("User", "alice");
var report = new CedarEntityUid("Document", "report");
var ownershipPolicies = CedarPolicySet.FromPolicies(
    ("owner-read", """permit(principal, action == Action::"read", resource) when { resource.owner == principal && context.trusted };"""));
using var contextJson = JsonDocument.Parse("""{"trusted":true}""");
var reportEntity = new CedarEntity(
    report,
    new { owner = CedarValue.Entity(alice) });

var factRequest = new CedarAuthorizationRequest(
    alice,
    new CedarEntityUid("Action", "read"),
    report,
    ownershipPolicies,
    contextJson.RootElement,
    new[] { reportEntity });

var factResult = engine.Authorize(factRequest);
Console.WriteLine(factResult.IsCleanAllow);
```

The typed `CedarEntity` convenience constructor above serializes an arbitrary CLR object using reflection. For trimming or NativeAOT, use its `JsonElement` constructor, or a source-generated `JsonTypeInfo<T>` through `CedarValue.FromObject`. The [NativeAOT sample](../samples/CedarSharp.AotSmoke/Program.cs) shows a complete reflection-free request.

## Inspect an unexpected result

```csharp
Console.WriteLine($"Decision: {result.Decision}");
foreach (var id in result.DeterminingPolicies)
    Console.WriteLine($"Determining policy: {id}");
foreach (var error in result.PolicyErrors)
    Console.WriteLine($"{error.PolicyId}: {error.Error.Message}");
foreach (var error in result.Errors)
    Console.WriteLine(error.Message);
```

A parse or evaluation failure can leave `Decision` null. Policy errors can accompany Allow. Native loading, ABI, transport, or response-decoding failures throw `CedarBridgeException`; they are not Cedar denials. Results also retain structured diagnostics and the upstream `Raw` JSON.

See the [API contract](api-contract.md) for exact result and exception behavior, [deployment](deployment.md) for supported targets, and the [documentation index](README.md) for deeper details.
