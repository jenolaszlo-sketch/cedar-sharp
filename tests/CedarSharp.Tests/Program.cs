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
if (args.Contains("--expect-load-ok"))
{
    try { return engine.GetVersion().Sha256.Length == 64 ? 0 : 1; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
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
    Throws<ArgumentNullException>(() => new CedarEntityUid("User", null!));
    Throws<ArgumentException>(() => new CedarEntityUid("Bad Type", "alice"));
    Throws<JsonException>(() => Request("", "{"));
    Throws<ArgumentException>(() => Request("", "[]"));
    Throws<ArgumentException>(() => engine.CheckContext("{}", schema));
    Throws<CedarInputException>(() => engine.CheckPolicies(CedarPolicySet.FromJson("{\"unknown\":true}")));
    Throws<CedarInputException>(() => engine.Authorize(Request("", "{\"x\":1,\"x\":2}")));
    Throws<ArgumentException>(() => CedarPolicySet.FromText(new string('x', 16 * 1024 * 1024 + 1)));
});
Add("empty cedar id is legal", () => {
    var empty = new CedarEntityUid("User", "");
    Assert(engine.Authorize(new(empty, read, report,
        CedarPolicySet.FromText("permit(principal == User::\"\", action, resource);"))).Decision == CedarDecision.Allow);
    Assert(engine.CheckEntities("""[{"uid":{"type":"User","id":""},"attrs":{},"parents":[]}]""").IsSuccess);
    Assert(empty.ToString() == "User::\"\"");
});
Add("entity uid cedar grammar", () => {
    var parsed = CedarEntityUid.Parse("User::\"alice\"");
    Assert(parsed.Type == "User" && parsed.Id == "alice");
    Assert(CedarEntityUid.TryParse("Acme::User::\"a\\\"b\"", out var namespaced) && namespaced!.Type == "Acme::User" && namespaced.Id == "a\"b");
    Assert(CedarEntityUid.TryParse("User::\"\\u{96ea}\"", out var unicode) && unicode!.Id == "雪");
    foreach (var invalid in new[] { "no-separator", "User::\"alice", "Bad Type::\"alice\"", "User::\"a\"x", "User::\"\\q\"", "User::alice" })
        Assert(!CedarEntityUid.TryParse(invalid, out _), $"should reject '{invalid}'");
    Assert(!CedarEntityUid.TryParse(null, out _));
    Assert(alice.ToString() == "User::\"alice\"");
});
Add("entity uid formatting is accepted by cedar", () => {
    foreach (var id in new[] { "alice", "雪", "a\"b", "a\\b", "<alice>", "line\nbreak", "" })
    {
        var uid = new CedarEntityUid("Acme::User", id);
        var policy = CedarPolicySet.FromText($"permit(principal == {uid}, action, resource);");
        var check = engine.CheckPolicies(policy);
        Assert(check.IsSuccess, $"Cedar rejected formatted uid for id '{id}': {check}");
    }
    Assert(new CedarEntityUid("User", "雪").ToString() == "User::\"\\u{96ea}\"");
});
Add("typed entities and extension values", () => {
    var entityRef = CedarValue.Entity(alice);
    Assert(entityRef.GetProperty("__entity").GetProperty("id").GetString() == "alice");
    Assert(CedarValue.Ip("127.0.0.1").GetProperty("__extn").GetProperty("fn").GetString() == "ip");
    var entities = new[] { new CedarEntity(report, new { owner = CedarValue.Entity(alice) }, new[] { new CedarEntityUid("Folder", "shared") }) };
    var req = new CedarAuthorizationRequest(alice, read, report, CedarPolicySet.FromText("permit(principal, action, resource);"),
        context: new { trusted = true }, entities: entities);
    Assert(req.Context.GetProperty("trusted").GetBoolean());
    Assert(req.Entities.GetArrayLength() == 1);
    Assert(engine.Authorize(req).Decision == CedarDecision.Allow);
    Assert(engine.CheckEntities(entities).IsSuccess);
    var entitySchema = CedarSchema.FromText("entity User; entity Folder; entity Document in Folder { owner: User };");
    var entityCheck = engine.CheckEntities(entities, entitySchema);
    Assert(entityCheck.IsSuccess, entityCheck.ToString());
    Assert(CedarEntity.ToJson(entities).Contains("shared"));
    Assert(new CedarEntity(report, (object?)null).Attrs.GetRawText() == "{}");
});
Add("fail-closed helpers distinguish allow with errors", () => {
    var policies = CedarPolicySet.FromPolicies(new Dictionary<string, string> {
        ["permit"] = "permit(principal, action, resource);", ["broken"] = "permit(principal, action, resource) when { principal.missing };" });
    var r = engine.Authorize(new(alice, read, report, policies));
    Assert(r.Decision == CedarDecision.Allow && !r.IsCleanAllow && !r.IsCleanDeny && !r.IsErrorFree);
    Throws<CedarAuthorizationException>(() => r.RequireAllow());
    Throws<CedarAuthorizationException>(() => r.EnsureNoErrors());
    var clean = engine.Authorize(new(alice, read, report, CedarPolicySet.FromText("permit(principal, action, resource);")));
    Assert(clean.IsCleanAllow && clean.RequireAllow() == CedarDecision.Allow);
    clean.EnsureNoErrors();
    var cleanDeny = engine.Authorize(Request(""));
    Assert(cleanDeny.IsCleanDeny && cleanDeny.IsErrorFree);
    cleanDeny.EnsureNoErrors();
    Throws<CedarAuthorizationException>(() => cleanDeny.RequireAllow());
    Assert(policies.PolicyIds.Contains("permit") && policies.PolicyIds.Contains("broken"));
    Assert(clean.ToString().Contains("Allow"));
});
Add("full request check combines scope context entities", () => {
    var req = Request("permit(principal, action, resource);", s: schema);
    var full = engine.CheckFullRequest(req, schema);
    Assert(full.IsSuccess && full.AllErrors.Count == 0);
    var bad = engine.CheckFullRequest(alice, new("Action", "missing"), report, "{}", "[]", schema);
    Assert(!bad.IsSuccess && bad.AllErrors.Count > 0);
    Assert(bad.ToString().Length > 0);
});
Add("batch preserves order and diagnostics", () => {
    var batch = engine.AuthorizeBatch(new[] { Request("permit(principal, action, resource);"), Request("") });
    Assert(batch.Count == 2 && batch[0].Decision == CedarDecision.Allow && batch[1].Decision == CedarDecision.Deny);
    var parallel = engine.AuthorizeBatch(new[] { Request("permit(principal, action, resource);"), Request("") }, parallel: true);
    Assert(parallel.Count == 2 && parallel[0].Decision == CedarDecision.Allow && parallel[1].Decision == CedarDecision.Deny);
});
Add("convenience overload and policy tuple helper", () => {
    var policies = CedarPolicySet.FromPolicies(("read", "permit(principal, action == Action::\"read\", resource);"));
    Assert(policies.PolicyIds.SequenceEqual(new[] { "read" }));
    var direct = engine.Authorize(alice, read, report, policies);
    Assert(direct.Decision == CedarDecision.Allow);
    var typed = engine.Authorize(alice, read, report, policies, contextJson: "{\"x\":1}");
    Assert(typed.Decision == CedarDecision.Allow);
});
Add("input value equality supports caching", () => {
    var a = CedarPolicySet.FromText("permit(principal, action, resource);");
    var b = CedarPolicySet.FromText("permit(principal, action, resource);");
    Assert(a.Equals(b) && a.GetHashCode() == b.GetHashCode() && !ReferenceEquals(a, b));
    Assert(!a.Equals(CedarPolicySet.Empty));
    var s1 = CedarSchema.FromText("entity User;");
    var s2 = CedarSchema.FromText("entity User;");
    Assert(s1.Equals(s2) && s1.GetHashCode() == s2.GetHashCode());
    Assert(s1.ToJsonString() == s2.ToJsonString());
    Assert(new HashSet<CedarPolicySet> { a, b }.Count == 1);
});
Add("activity source emits authorization spans", () => {
    var seen = new List<string>();
    using var listener = new System.Diagnostics.ActivityListener {
        ShouldListenTo = s => s.Name == CedarSharpDiagnostics.ActivitySourceName,
        Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
        ActivityStarted = a => seen.Add(a.DisplayName)
    };
    System.Diagnostics.ActivitySource.AddActivityListener(listener);
    engine.Authorize(Request("permit(principal, action, resource);"));
    engine.ValidatePolicies(CedarPolicySet.FromText("permit(principal, action, resource);"), schema);
    Assert(seen.Contains("cedarsharp.authorize") && seen.Contains("cedarsharp.validate_policies"));
});
Add("validation ensure and version formatting", () => {
    var ok = engine.ValidatePolicies(CedarPolicySet.FromText("permit(principal, action, resource);"), schema);
    ok.EnsureValid();
    var bad = engine.ValidatePolicies(CedarPolicySet.FromText("permit(principal, action, resource) when { principal.unknown };"), schema);
    Throws<CedarValidationException>(() => bad.EnsureValid());
    Assert(engine.GetVersion().ToString().Contains("Cedar 4.13.0"));
});
Add("concurrent isolation and repeated output ownership", () => {
    Parallel.For(0, 500, i => {
        var expect = i % 2 == 0 ? CedarDecision.Allow : CedarDecision.Deny;
        var result = engine.Authorize(Request(i % 2 == 0 ? "permit(principal, action, resource);" : ""));
        Assert(result.Decision == expect && result.PolicyErrors.Count == 0);
    });
});
Add("strict decoding rejects malformed responses", () => {
    static JsonElement Doc(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    Throws<JsonException>(() => new CedarValidationResult(Doc("""{"type":"success","validationErrors":42,"validationWarnings":[],"otherWarnings":[]}""")));
    Throws<JsonException>(() => new CedarValidationResult(Doc("""{"type":"success","validationErrors":[],"validationWarnings":42,"otherWarnings":[]}""")));
    Throws<JsonException>(() => new CedarValidationResult(Doc("""{"type":"success","validationErrors":[],"validationWarnings":[]}""")));
    var valid = new CedarValidationResult(Doc("""{"type":"success","validationErrors":[],"validationWarnings":[],"otherWarnings":[]}"""));
    Assert(valid.IsSuccess && valid.IsValid);
    Throws<JsonException>(() => new CedarAuthorizationResult(Doc("""{"type":"success","response":{"decision":"allow","diagnostics":{"reason":42,"errors":[]}}}""")));
    Throws<JsonException>(() => new CedarAuthorizationResult(Doc("""{"type":"success","response":{"decision":"allow","diagnostics":{"reason":[]}}}""")));
    Throws<JsonException>(() => new CedarAuthorizationResult(Doc("""{"type":"bogus"}""")));
    var auth = new CedarAuthorizationResult(Doc("""{"type":"success","response":{"decision":"deny","diagnostics":{"reason":[],"errors":[]}}}"""));
    Assert(auth.Decision == CedarDecision.Deny && auth.IsSuccess);
    Throws<JsonException>(() => new CedarCheckResult(Doc("""{"type":"failure"}""")));
});
Add("typed input validation", () => {
    Throws<ArgumentException>(() => CedarPolicySet.FromPolicies(new[] {
        new KeyValuePair<string, string>("a", "permit(principal, action, resource);"),
        new KeyValuePair<string, string>("a", "permit(principal, action, resource);") }));
    Throws<ArgumentNullException>(() => new CedarEntity(alice, "{}", new CedarEntityUid[] { null! }));
    Throws<ArgumentNullException>(() => new CedarAuthorizationRequest(alice, read, report, CedarPolicySet.Empty, "{}", new CedarEntity[] { null! }));
    var raw = CedarPolicySet.FromJson("""{"templateLinks":[1]}""");
    Assert(raw.PolicyIds.Count == 0 && raw.ToString().Length > 0);
    var context = JsonSerializer.SerializeToElement(new { trusted = true });
    var req = new CedarAuthorizationRequest(alice, read, report,
        CedarPolicySet.FromText("permit(principal, action, resource);"), context, new[] { new CedarEntity(report, "{}") });
    Assert(engine.Authorize(req).Decision == CedarDecision.Allow);
    Assert(engine.CheckContext(JsonSerializer.SerializeToElement(new { }), schema, read).IsSuccess);
});
Add("telemetry defaults omit identity", () => {
    var activities = new List<Activity>();
    using var listener = new System.Diagnostics.ActivityListener {
        ShouldListenTo = s => s.Name == CedarSharpDiagnostics.ActivitySourceName,
        Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
        ActivityStarted = a => activities.Add(a)
    };
    System.Diagnostics.ActivitySource.AddActivityListener(listener);
    engine.Authorize(Request("permit(principal, action, resource);"));
    Assert(activities.Count > 0 && activities.All(a => a.GetTagItem("cedar.principal") is null));
    Assert(activities.All(a => a.GetTagItem("cedar.decision") is not null));
    CedarSharpDiagnostics.IncludeIdentity = true;
    try
    {
        engine.Authorize(Request("permit(principal, action, resource);"));
        Assert(activities.Any(a => a.GetTagItem("cedar.principal") is not null));
    }
    finally { CedarSharpDiagnostics.IncludeIdentity = false; }
});
Add("relative native override resolves adjacent manifest", () => {
    var native = engine.GetVersion().NativePath;
    var directory = Path.Combine(Path.GetTempPath(), "cedarsharp-override-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var fileName = Path.GetFileName(native);
        File.Copy(native, Path.Combine(directory, fileName));
        File.Copy(Path.Combine(Path.GetDirectoryName(native)!, "cedarsharp-native.json"), Path.Combine(directory, "cedarsharp-native.json"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = directory };
        start.Environment["CEDARSHARP_NATIVE_PATH"] = fileName;
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CedarSharp.Tests.dll"));
        start.ArgumentList.Add("--expect-load-ok");
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("Override probe timed out"); }
        Assert(process.ExitCode == 0, "Relative native override did not resolve the adjacent manifest");
    }
    finally { Directory.Delete(directory, recursive: true); }
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
            start.Environment["CEDARSHARP_NATIVE_PATH"] = "";
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
