using System.Text.Json;
using CedarSharp;

// This sample uses only trim/NativeAOT-safe APIs: JsonElement context and
// entities, string policy/schema input, and no reflection-based serialization.
var engine = new CedarEngine();
var alice = new CedarEntityUid("User", "alice");
var read = new CedarEntityUid("Action", "read");
var report = new CedarEntityUid("Document", "report");

var schema = CedarSchema.FromText("""
    entity User;
    entity Document;
    action read appliesTo { principal: User, resource: Document, context: { trusted: Bool } };
    """);
var policies = CedarPolicySet.FromPolicies(("read", "permit(principal, action == Action::\"read\", resource) when { context.trusted };"));

using var contextDocument = JsonDocument.Parse("""{"trusted":true}""");
using var entitiesDocument = JsonDocument.Parse("""[{"uid":{"type":"Document","id":"report"},"attrs":{},"parents":[]}]""");
var request = new CedarAuthorizationRequest(
    alice, read, report, policies,
    contextDocument.RootElement, entitiesDocument.RootElement, schema);

var result = engine.Authorize(request);
if (!result.IsCleanAllow)
{
    throw new InvalidOperationException($"Expected a clean allow, received {result}.");
}

engine.ValidatePolicies(policies, schema).EnsureValid();

var check = engine.CheckFullRequest(request);
if (!check.IsSuccess)
{
    throw new InvalidOperationException($"Expected a valid request, received {check}.");
}

Console.WriteLine($"CedarSharp NativeAOT smoke passed: {engine.GetVersion()}.");
