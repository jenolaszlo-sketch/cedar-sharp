using System.Text.Json;

namespace CedarSharp;

/// <summary>A Cedar entity identifier. Cedar validates the type name during evaluation.</summary>
public sealed record CedarEntityUid
{
    public string Type { get; }
    public string Id { get; }
    public CedarEntityUid(string type, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(id);
        JsonWire.StrictUtf8.GetByteCount(type); JsonWire.StrictUtf8.GetByteCount(id);
        Type = type;
        Id = id;
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("id", Id);
        writer.WriteEndObject();
    }
}

/// <summary>An immutable policy input, including optional templates and links. Construction does not parse Cedar.</summary>
public sealed class CedarPolicySet
{
    internal JsonElement Value { get; }
    private CedarPolicySet(JsonElement value) => Value = value;
    public static CedarPolicySet FromText(string policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        JsonWire.StrictUtf8.GetByteCount(policies);
        return new(JsonWire.Build(w => { w.WriteStartObject(); w.WriteString("staticPolicies", policies); w.WriteEndObject(); }));
    }
    /// <summary>Assign stable policy IDs that appear in determining policies and diagnostics.</summary>
    public static CedarPolicySet FromPolicies(IReadOnlyDictionary<string, string> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        return new(JsonWire.Build(w => {
            w.WriteStartObject(); w.WriteStartObject("staticPolicies");
            foreach (var (id, text) in policies)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(id);
                ArgumentNullException.ThrowIfNull(text);
                JsonWire.StrictUtf8.GetByteCount(id); JsonWire.StrictUtf8.GetByteCount(text);
                w.WriteString(id, text);
            }
            w.WriteEndObject(); w.WriteEndObject();
        }));
    }
    /// <summary>Accepts the upstream FFI policy-set object: staticPolicies, templates, templateLinks.</summary>
    public static CedarPolicySet FromJson(string json) => new(JsonWire.Parse(json, JsonValueKind.Object));
}

/// <summary>An immutable schema input in Cedar schema syntax or Cedar JSON schema format.</summary>
public sealed class CedarSchema
{
    internal JsonElement Value { get; }
    private CedarSchema(JsonElement value) => Value = value;
    public static CedarSchema FromText(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        JsonWire.StrictUtf8.GetByteCount(schema);
        return new(JsonWire.Build(w => w.WriteStringValue(schema)));
    }
    public static CedarSchema FromJson(string json) => new(JsonWire.Parse(json, JsonValueKind.Object));
}

/// <summary>One immutable request snapshot. JSON inputs are copied and use Cedar's entity/value encoding.</summary>
public sealed class CedarAuthorizationRequest
{
    public CedarEntityUid Principal { get; }
    public CedarEntityUid Action { get; }
    public CedarEntityUid Resource { get; }
    public CedarPolicySet Policies { get; }
    public CedarSchema? Schema { get; }
    /// <summary>When a schema is provided, validate the request against it. Context and entities still use schema-based parsing.</summary>
    public bool ValidateRequest { get; }
    public JsonElement Context { get; }
    public JsonElement Entities { get; }
    public CedarAuthorizationRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        CedarPolicySet policies, string contextJson = "{}", string entitiesJson = "[]",
        CedarSchema? schema = null, bool validateRequest = true)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(policies);
        Principal = principal; Action = action; Resource = resource; Policies = policies;
        Context = JsonWire.Parse(contextJson, JsonValueKind.Object);
        Entities = JsonWire.Parse(entitiesJson, JsonValueKind.Array);
        Schema = schema; ValidateRequest = validateRequest;
    }
    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WritePropertyName("principal"); Principal.Write(w);
        w.WritePropertyName("action"); Action.Write(w);
        w.WritePropertyName("resource"); Resource.Write(w);
        w.WritePropertyName("policies"); Policies.Value.WriteTo(w);
        w.WritePropertyName("context"); Context.WriteTo(w);
        w.WritePropertyName("entities"); Entities.WriteTo(w);
        if (Schema is not null) { w.WritePropertyName("schema"); Schema.Value.WriteTo(w); }
        w.WriteBoolean("validateRequest", ValidateRequest);
        w.WriteEndObject();
    }
}