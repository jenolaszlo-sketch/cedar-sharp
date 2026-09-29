using System.Diagnostics;
using System.Text.Json;
using CedarSharp;

var engine = new CedarEngine();
var tests = new List<(string Name, Action Test)>();
var alice = new CedarEntityUid("User", "alice");
var read = new CedarEntityUid("Action", "read");
var report = new CedarEntityUid("Document", "report");
var schema = CedarSchema.FromText("entity User; entity Document; action read appliesTo { principal: User, resource: Document, context: {} };");
CedarAuthorizationRequest Request(string policies, string context = "{}", string entities = "[]", CedarSchema? s = null) =>
    new(alice, read, report, CedarPolicySet.FromText(policies), context, entities, s);
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
void Add(string name, Action test) => tests.Add((name, test));

// Loader rejection checks run in fresh processes because native loading is intentionally process-wide.
if (args.Contains("--expect-load-failure"))
{
    try { engine.GetVersion(); Console.Error.WriteLine("Unexpected successful load"); return 1; }
    catch (CedarBridgeException) { return 0; }
}
Add("loaded identity", () => {
    var v = engine.GetVersion();
    Assert(v.AbiVersion == 1 && v.SdkVersion == "4.13.0" && v.BridgeVersion == "0.1.0");
    Assert(v.Features.Count == 3 && v.Sha256.Length == 64 && File.Exists(v.NativePath));
    Console.WriteLine($"Native: {v.SdkVersion}; language {v.LanguageVersion}; {v.Target}; SHA256 {v.Sha256}");
});
Add("default deny", () => {
    var r = engine.Authorize(Request(""));
    Assert(r.IsSuccess && r.Decision == CedarDecision.Deny && r.DeterminingPolicies.Count == 0 && r.PolicyErrors.Count == 0);
});
Add("named permit and forbid precedence", () => {
    var policies = CedarPolicySet.FromPolicies(new Dictionary<string, string> {
        ["allow-read"] = "permit(principal, action, resource);", ["mandatory-deny"] = "forbid(principal, action, resource);" });
    var r = engine.Authorize(new(alice, read, report, policies));
    Assert(r.Decision == CedarDecision.Deny && r.DeterminingPolicies.SequenceEqual(new[] { "mandatory-deny" }));
});
Add("policy inputs are copied snapshots", () => {
    var source = new Dictionary<string, string> { ["allow"] = "permit(principal, action, resource);" };
    var policies = CedarPolicySet.FromPolicies(source);
    source["allow"] = "forbid(principal, action, resource);";
    source.Clear();
    var result = engine.Authorize(new(alice, read, report, policies));
    Assert(result.Decision == CedarDecision.Allow && result.DeterminingPolicies.Contains("allow"));
});
Add("conditions and default deny", () => {
    var policy = "permit(principal, action, resource) when { context.trusted };";
    Assert(engine.Authorize(Request(policy, "{\"trusted\":true}")).Decision == CedarDecision.Allow);
    Assert(engine.Authorize(Request(policy, "{\"trusted\":false}")).Decision == CedarDecision.Deny);
});
Add("hierarchy", () => {
    const string entities = """[{"uid":{"type":"Document","id":"report"},"attrs":{},"parents":[{"type":"Folder","id":"shared"}]}]""";
    Assert(engine.Authorize(Request("permit(principal, action, resource in Folder::\"shared\");", entities: entities)).Decision == CedarDecision.Allow);
});
Add("allow with full error diagnostics", () => {
    var policies = CedarPolicySet.FromPolicies(new Dictionary<string, string> {
        ["permit"] = "permit(principal, action, resource);", ["broken"] = "permit(principal, action, resource) when { principal.missing };" });
    var r = engine.Authorize(new(alice, read, report, policies));
    Assert(r.IsSuccess && r.Decision == CedarDecision.Allow && r.Errors.Count == 0 && r.PolicyErrors.Count == 1);
    Assert(r.PolicyErrors[0].PolicyId == "broken" && r.PolicyErrors[0].Error.Message.Length > 0);
    Assert(r.PolicyErrors[0].Error.SourceLocations.Count > 0 && r.Raw.GetProperty("response").GetProperty("diagnostics").GetProperty("errors").GetArrayLength() == 1);
});
Add("malformed policy has no decision", () => {
    var r = engine.Authorize(Request("this is invalid"));
    Assert(!r.IsSuccess && r.Decision is null && r.Errors.Count > 0);
});
Add("parsing distinct from strict validation", () => {
    var valid = CedarPolicySet.FromText("permit(principal, action == Action::\"read\", resource);");
    Assert(engine.CheckSchema(schema).IsSuccess && engine.CheckPolicies(valid).IsSuccess);
    Assert(engine.ValidatePolicies(valid, schema).IsValid);
    var invalid = CedarPolicySet.FromText("permit(principal, action, resource) when { principal.unknown };" );
    Assert(engine.CheckPolicies(invalid).IsSuccess);
    var result = engine.ValidatePolicies(invalid, schema);
    Assert(result.IsSuccess && !result.IsValid && result.ValidationErrors.Count > 0);
    var malformed = engine.ValidatePolicies(CedarPolicySet.FromText("bad"), schema);
    Assert(!malformed.IsSuccess && !malformed.IsValid && malformed.Errors.Count > 0);
});
Add("schema JSON and malformed schema", () => {
    Assert(engine.CheckSchema(CedarSchema.FromJson("{}")).IsSuccess);
    Assert(!engine.CheckSchema(CedarSchema.FromText("invalid schema")).IsSuccess);
});
Add("request context and entities validation", () => {
    Assert(engine.CheckRequest(alice, read, report, schema).IsSuccess);
    Assert(!engine.CheckRequest(alice, new("Action", "missing"), report, schema).IsSuccess);
    Assert(engine.CheckContext("{}", schema, read).IsSuccess);
    Assert(!engine.CheckContext("{\"unexpected\":true}", schema, read).IsSuccess);
    Assert(engine.CheckEntities("[]", schema).IsSuccess);
    Assert(!engine.CheckEntities("""[{"uid":{"type":"Unknown","id":"x"},"attrs":{},"parents":[]}]""", schema).IsSuccess);
    Assert(engine.Authorize(Request("permit(principal, action, resource);", s: schema)).Decision == CedarDecision.Allow);
});
Add("schema request opt out is explicit", () => {
    var invalidPrincipal = new CedarEntityUid("Other", "alice");
    var policies = CedarPolicySet.FromText("permit(principal, action, resource);");
    Assert(!engine.Authorize(new(invalidPrincipal, read, report, policies, schema: schema)).IsSuccess);
    Assert(engine.Authorize(new(invalidPrincipal, read, report, policies, schema: schema, validateRequest: false)).Decision == CedarDecision.Allow);
});
Add("template linking", () => {
    var policies = CedarPolicySet.FromJson("""{"templates":{"t":"permit(principal == ?principal, action, resource);"},"templateLinks":[{"templateId":"t","newId":"linked","values":{"?principal":{"type":"User","id":"alice"}}}]}""");
    var r = engine.Authorize(new(alice, read, report, policies));
    Assert(r.Decision == CedarDecision.Allow && r.DeterminingPolicies.Contains("linked"));
});
Add("unicode and embedded NUL preserve entity IDs", () => {
    var who = new CedarEntityUid("User", "álîce\0雪");
    Assert(engine.Authorize(new(who, read, report, CedarPolicySet.FromText("permit(principal, action, resource) when { principal == principal };"))).Decision == CedarDecision.Allow);
});
Add("invalid input does not authorize", () => {
    Throws<System.Text.EncoderFallbackException>(() => new CedarEntityUid("User", "\ud800"));
    Throws<JsonException>(() => Request("", "{"));
    Throws<ArgumentException>(() => Request("", "[]"));
    Throws<ArgumentException>(() => engine.CheckContext("{}", schema));
    Throws<CedarInputException>(() => engine.CheckPolicies(CedarPolicySet.FromJson("{\"unknown\":true}")));
    Throws<CedarInputException>(() => engine.Authorize(Request("", "{\"x\":1,\"x\":2}")));
    Throws<ArgumentException>(() => CedarPolicySet.FromText(new string('x', 16 * 1024 * 1024 + 1)));
});
Add("concurrent isolation and repeated output ownership", () => {
    Parallel.For(0, 500, i => {
        var expect = i % 2 == 0 ? CedarDecision.Allow : CedarDecision.Deny;
        var result = engine.Authorize(Request(i % 2 == 0 ? "permit(principal, action, resource);" : ""));
        Assert(result.Decision == expect && result.PolicyErrors.Count == 0);
    });
});
Add("reject missing tampered and incompatible assets", () => {
    var source = AppContext.BaseDirectory;
    var temporary = Path.Combine(Path.GetTempPath(), "cedarsharp-loader-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporary);
    try
    {
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(temporary, Path.GetFileName(file)));
        var native = engine.GetVersion().NativePath;
        var manifestPath = Path.Combine(Path.GetDirectoryName(native)!, "cedarsharp-native.json");
        var manifest = File.ReadAllText(manifestPath);
        var destLibrary = Path.Combine(temporary, Path.GetFileName(native));
        var destManifest = Path.Combine(temporary, "cedarsharp-native.json");
        if (File.Exists(destLibrary)) File.Delete(destLibrary);
        void Fails()
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(Path.Combine(temporary, "CedarSharp.Tests.dll")); start.ArgumentList.Add("--expect-load-failure");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("Loader probe timed out"); }
            Assert(process.ExitCode == 0, "Loader accepted invalid asset");
        }
        Fails();
        File.Copy(native, destLibrary); File.WriteAllText(destManifest, manifest.Replace(engine.GetVersion().Sha256, new string('0', 64), StringComparison.OrdinalIgnoreCase));
        Fails();
        var node = System.Text.Json.Nodes.JsonNode.Parse(manifest)!;
        node["abiVersion"] = 999; File.WriteAllText(destManifest, node.ToJsonString());
        Fails();
    }
    finally { Directory.Delete(temporary, recursive: true); }
});
var failed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} tests passed on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
return failed == 0 ? 0 : 1;
