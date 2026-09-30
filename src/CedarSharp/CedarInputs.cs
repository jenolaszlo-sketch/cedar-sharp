using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace CedarSharp;

/// <summary>A Cedar entity identifier. Cedar validates the type name during evaluation.</summary>
public sealed record CedarEntityUid
{
    /// <summary>Cedar entity type, including a namespace when applicable (for example <c>Acme::User</c>).</summary>
    public string Type { get; }
    /// <summary>Cedar entity identifier within its type.</summary>
    public string Id { get; }
    /// <summary>Creates an entity UID; Cedar checks the type name when processing a call. An empty id is a legal Cedar id.</summary>
    /// <exception cref="ArgumentException">Type is blank or not a Cedar identifier path, or id is null.</exception>
    public CedarEntityUid(string type, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!CedarText.IsIdentifierPath(type)) throw new ArgumentException($"'{type}' is not a Cedar entity type path.", nameof(type));
        JsonWire.StrictUtf8.GetByteCount(type); JsonWire.StrictUtf8.GetByteCount(id);
        Type = type;
        Id = id;
    }
    /// <summary>Parses Cedar source text such as <c>User::"alice"</c> or <c>Acme::User::"a\u{96ea}"</c> using Cedar string escapes.</summary>
    /// <exception cref="FormatException">The text is not a Cedar entity UID literal.</exception>
    public static CedarEntityUid Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!TryParse(text, out var uid) || uid is null) throw new FormatException($"Not a Cedar entity UID literal: '{text}'. Expected Type::\"id\".");
        return uid;
    }
    /// <summary>Tries to parse Cedar source text such as <c>User::"alice"</c>. This is Cedar grammar, not JSON string syntax.</summary>
    public static bool TryParse(string? text, out CedarEntityUid? uid)
    {
        uid = null;
        if (string.IsNullOrEmpty(text)) return false;
        var quote = text.IndexOf('"');
        if (quote < 2 || text[quote - 1] != ':' || text[quote - 2] != ':') return false;
        var type = text[..(quote - 2)];
        if (!CedarText.IsIdentifierPath(type)) return false;
        if (!CedarText.TryUnescapeQuoted(text, quote, out var id, out var end)) return false;
        if (end != text.Length) return false;
        uid = new CedarEntityUid(type, id);
        return true;
    }
    /// <summary>Cedar source form: <c>Type::"id"</c> using Cedar string escapes (not JSON escapes).</summary>
    public override string ToString() => $"{Type}::\"{CedarText.Escape(Id)}\"";
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("type", Type);
        writer.WriteString("id", Id);
        writer.WriteEndObject();
    }
}

/// <summary>An immutable policy input, including optional templates and links. Construction does not parse Cedar.</summary>
public sealed class CedarPolicySet : IEquatable<CedarPolicySet>
{
    internal JsonElement Value { get; }
    private readonly string _raw;
    private CedarPolicySet(JsonElement value) { Value = value; _raw = value.GetRawText(); }
    /// <summary>An empty policy set (default-deny).</summary>
    public static CedarPolicySet Empty { get; } = FromText("");
    /// <summary>IDs assigned by <see cref="FromPolicies(System.Collections.Generic.IReadOnlyDictionary{string,string})"/> or <c>templateLinks</c>; empty for plain-text sets.</summary>
    public IReadOnlyList<string> PolicyIds
    {
        get
        {
            if (Value.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
            var ids = new List<string>();
            if (Value.TryGetProperty("staticPolicies", out var sp) && sp.ValueKind == JsonValueKind.Object)
                foreach (var p in sp.EnumerateObject()) ids.Add(p.Name);
            if (Value.TryGetProperty("templateLinks", out var links) && links.ValueKind == JsonValueKind.Array)
                foreach (var l in links.EnumerateArray())
                    if (l.ValueKind == JsonValueKind.Object && l.TryGetProperty("newId", out var id) &&
                        id.ValueKind == JsonValueKind.String && id.GetString() is { } s) ids.Add(s);
            return ids.AsReadOnly();
        }
    }
    /// <summary>Creates a snapshot from Cedar policy text. Use FromPolicies when stable caller-assigned policy IDs are needed.</summary>
    public static CedarPolicySet FromText(string policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        JsonWire.StrictUtf8.GetByteCount(policies);
        return new(JsonWire.Build(w => { w.WriteStartObject(); w.WriteString("staticPolicies", policies); w.WriteEndObject(); }));
    }
    /// <summary>Assign stable policy IDs that appear in determining policies and diagnostics.</summary>
    public static CedarPolicySet FromPolicies(IReadOnlyDictionary<string, string> policies)
        => FromPolicies((IEnumerable<KeyValuePair<string, string>>)policies);
    /// <summary>Assign stable policy IDs from <c>(id, text)</c> tuples, for example <c>FromPolicies(("read", "..."), ("write", "..."))</c>.</summary>
    public static CedarPolicySet FromPolicies(params (string Id, string Text)[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        return FromPolicies(policies.Select(p => new KeyValuePair<string, string>(p.Id, p.Text)));
    }
    /// <summary>Assign stable policy IDs from any ordered sequence. Duplicate IDs are rejected.</summary>
    public static CedarPolicySet FromPolicies(IEnumerable<KeyValuePair<string, string>> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new(JsonWire.Build(w => {
            w.WriteStartObject(); w.WriteStartObject("staticPolicies");
            foreach (var (id, text) in policies)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(id);
                ArgumentNullException.ThrowIfNull(text);
                if (!seen.Add(id)) throw new ArgumentException($"Duplicate policy ID '{id}'.", nameof(policies));
                JsonWire.StrictUtf8.GetByteCount(id); JsonWire.StrictUtf8.GetByteCount(text);
                w.WriteString(id, text);
            }
            w.WriteEndObject(); w.WriteEndObject();
        }));
    }
    /// <summary>Accepts the upstream FFI policy-set object: staticPolicies, templates, templateLinks.</summary>
    public static CedarPolicySet FromJson(string json) => new(JsonWire.Parse(json, JsonValueKind.Object));
    /// <summary>Accepts the upstream FFI policy-set object from an already-parsed element.</summary>
    public static CedarPolicySet FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(json));
        return new(json.Clone());
    }
    /// <summary>Raw upstream JSON for logging or round-tripping.</summary>
    public string ToJsonString() => _raw;
    /// <summary>Value equality by upstream JSON, so policy sets can key caches and memoized validation.</summary>
    public bool Equals(CedarPolicySet? other) => other is not null && string.Equals(_raw, other._raw, StringComparison.Ordinal);
    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CedarPolicySet);
    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_raw);
    /// <inheritdoc />
    public override string ToString() => $"PolicySet({string.Join(",", PolicyIds)})";
}

/// <summary>An immutable schema input in Cedar schema syntax or Cedar JSON schema format.</summary>
public sealed class CedarSchema : IEquatable<CedarSchema>
{
    internal JsonElement Value { get; }
    private readonly string _raw;
    /// <summary>True when created from Cedar schema syntax text; false for JSON schemas.</summary>
    public bool IsTextSchema => Value.ValueKind == JsonValueKind.String;
    private CedarSchema(JsonElement value) { Value = value; _raw = value.GetRawText(); }
    /// <summary>Creates a snapshot of a schema written in Cedar schema syntax.</summary>
    public static CedarSchema FromText(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        JsonWire.StrictUtf8.GetByteCount(schema);
        return new(JsonWire.Build(w => w.WriteStringValue(schema)));
    }
    /// <summary>Creates a snapshot of a Cedar JSON schema object.</summary>
    public static CedarSchema FromJson(string json) => new(JsonWire.Parse(json, JsonValueKind.Object));
    /// <summary>Creates a snapshot of a Cedar JSON schema from an already-parsed element.</summary>
    public static CedarSchema FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(json));
        return new(json.Clone());
    }
    /// <summary>Raw upstream JSON for logging or round-tripping.</summary>
    public string ToJsonString() => _raw;
    /// <summary>Value equality by upstream JSON, so schemas can key caches and memoized validation.</summary>
    public bool Equals(CedarSchema? other) => other is not null && string.Equals(_raw, other._raw, StringComparison.Ordinal);
    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CedarSchema);
    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_raw);
    /// <inheritdoc />
    public override string ToString() => IsTextSchema ? "Schema(text)" : "Schema(json)";
}

/// <summary>Helpers for Cedar's JSON value encoding: entity references and extension values.</summary>
public static class CedarValue
{
    /// <summary>Builds <c>{"__entity":{"type":..,"id":..}}</c> for use inside context or entity attributes.</summary>
    public static JsonElement Entity(CedarEntityUid uid)
    {
        ArgumentNullException.ThrowIfNull(uid);
        return JsonWire.Build(w => {
            w.WriteStartObject(); w.WriteStartObject("__entity");
            w.WriteString("type", uid.Type); w.WriteString("id", uid.Id);
            w.WriteEndObject(); w.WriteEndObject();
        });
    }
    /// <summary>Builds an entity reference from type and id.</summary>
    public static JsonElement Entity(string type, string id) => Entity(new CedarEntityUid(type, id));
    /// <summary>Builds <c>{"__extn":{"fn":..,"arg":..}}</c> for <c>ip</c>, <c>decimal</c>, <c>datetime</c> literals.</summary>
    public static JsonElement Extension(string function, JsonElement argument)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(function);
        return JsonWire.Build(w => {
            w.WriteStartObject(); w.WriteStartObject("__extn");
            w.WriteString("fn", function); w.WritePropertyName("arg"); argument.WriteTo(w);
            w.WriteEndObject(); w.WriteEndObject();
        });
    }
    /// <summary>Builds an <c>ip("...")</c> extension value.</summary>
    public static JsonElement Ip(string address) => Extension("ip", JsonWire.StringElement(address));
    /// <summary>Builds a <c>decimal("...")</c> extension value.</summary>
    public static JsonElement Decimal(string value) => Extension("decimal", JsonWire.StringElement(value));
    /// <summary>Builds a <c>datetime("...")</c> extension value.</summary>
    public static JsonElement Datetime(string value) => Extension("datetime", JsonWire.StringElement(value));
    /// <summary>Copies a JSON object/array element for context or attributes.</summary>
    public static JsonElement FromObject(JsonElement value) => JsonWire.FromObject(value);
    /// <summary>Copies a JSON node for context or attributes.</summary>
    public static JsonElement FromNode(JsonNode? node) => JsonWire.FromNode(node);
    /// <summary>Serializes a value using caller-supplied source-generated metadata. Trim/AOT-safe.</summary>
    public static JsonElement FromObject<T>(T value, JsonTypeInfo<T> typeInfo) => JsonWire.FromObject(value, typeInfo);
    /// <summary>Serializes an anonymous object, dictionary, or record to a copied <see cref="JsonElement"/>. Uses reflection.</summary>
    [RequiresUnreferencedCode("Serializing arbitrary CLR objects uses reflection. Use JsonElement, JsonNode, or the JsonTypeInfo overload for trim/AOT.")]
    [RequiresDynamicCode("Serializing arbitrary CLR objects uses reflection. Use JsonElement, JsonNode, or the JsonTypeInfo overload for trim/AOT.")]
    public static JsonElement FromObject<T>(T? value) => JsonWire.FromObjectReflection(value);
}

/// <summary>One Cedar entity: UID plus attributes and parent hierarchy.</summary>
public sealed class CedarEntity
{
    /// <summary>Entity UID.</summary>
    public CedarEntityUid Uid { get; }
    /// <summary>Copied Cedar-encoded attributes object.</summary>
    public JsonElement Attrs { get; }
    /// <summary>Parent UIDs for hierarchy (<c>in</c>) checks.</summary>
    public IReadOnlyList<CedarEntityUid> Parents { get; }
    /// <summary>Creates an entity from a JSON attributes string.</summary>
    public CedarEntity(CedarEntityUid uid, string attrsJson = "{}", IEnumerable<CedarEntityUid>? parents = null)
        : this(uid, JsonWire.Parse(attrsJson, JsonValueKind.Object), parents) { }
    /// <summary>Creates an entity from a copied attributes element.</summary>
    public CedarEntity(CedarEntityUid uid, JsonElement attrs, IEnumerable<CedarEntityUid>? parents = null)
    {
        ArgumentNullException.ThrowIfNull(uid);
        if (attrs.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(attrs));
        Uid = uid; Attrs = attrs.Clone();
        var parentArray = parents?.ToArray() ?? Array.Empty<CedarEntityUid>();
        foreach (var parent in parentArray) ArgumentNullException.ThrowIfNull(parent, nameof(parents));
        Parents = parentArray.AsReadOnly();
    }
    /// <summary>Creates an entity from an anonymous object, dictionary, or record serialized as attributes. A null value means no attributes. Uses reflection; use the <see cref="JsonElement"/> overload for trim/AOT.</summary>
    [RequiresUnreferencedCode("Serializing arbitrary CLR objects uses reflection. Use the JsonElement overload for trim/AOT.")]
    [RequiresDynamicCode("Serializing arbitrary CLR objects uses reflection. Use the JsonElement overload for trim/AOT.")]
    public CedarEntity(CedarEntityUid uid, object? attrs, IEnumerable<CedarEntityUid>? parents = null)
        : this(uid, attrs is null ? JsonWire.Parse("{}", JsonValueKind.Object) : JsonWire.FromObjectReflection(attrs) is { ValueKind: JsonValueKind.Object } e ? e : throw new ArgumentException("Attributes must serialize to a JSON Object.", nameof(attrs)), parents) { }
    /// <summary>Serializes entities to a Cedar entities JSON array string.</summary>
    public static string ToJson(IEnumerable<CedarEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartArray();
            foreach (var e in entities) e.Write(w);
            w.WriteEndArray();
        }
        return JsonWire.StrictUtf8.GetString(stream.ToArray());
    }
    internal void Write(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WritePropertyName("uid"); Uid.Write(w);
        w.WritePropertyName("attrs"); Attrs.WriteTo(w);
        w.WriteStartArray("parents");
        foreach (var p in Parents) { p.Write(w); }
        w.WriteEndArray();
        w.WriteEndObject();
    }
}

/// <summary>One immutable request snapshot. JSON inputs are copied and use Cedar's entity/value encoding.</summary>
public sealed class CedarAuthorizationRequest
{
    /// <summary>The principal performing the action.</summary>
    public CedarEntityUid Principal { get; }
    /// <summary>The action being performed.</summary>
    public CedarEntityUid Action { get; }
    /// <summary>The resource on which the action is performed.</summary>
    public CedarEntityUid Resource { get; }
    /// <summary>The immutable policy set evaluated for this request.</summary>
    public CedarPolicySet Policies { get; }
    /// <summary>Optional schema used to parse context and entities and, by default, validate the request.</summary>
    public CedarSchema? Schema { get; }
    /// <summary>When a schema is provided, validate the request against it. Context and entities still use schema-based parsing.</summary>
    public bool ValidateRequest { get; }
    /// <summary>A copied Cedar-encoded JSON context object.</summary>
    public JsonElement Context { get; }
    /// <summary>A copied Cedar-encoded JSON array of entities.</summary>
    public JsonElement Entities { get; }
    /// <summary>Creates an immutable request snapshot from Cedar entity UIDs and JSON context/entities.</summary>
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
    /// <summary>Creates a request from already-parsed context/entities elements (copied).</summary>
    public CedarAuthorizationRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        CedarPolicySet policies, JsonElement context, JsonElement entities,
        CedarSchema? schema = null, bool validateRequest = true)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(policies);
        if (context.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(context));
        if (entities.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected a JSON Array.", nameof(entities));
        Principal = principal; Action = action; Resource = resource; Policies = policies;
        Context = context.Clone(); Entities = entities.Clone();
        Schema = schema; ValidateRequest = validateRequest;
    }
    /// <summary>Creates a request from typed entities and a serializable context object. Uses reflection; prefer the <see cref="JsonElement"/> overloads for trim/AOT.</summary>
    /// <example>
    /// <code>
    /// var request = new CedarAuthorizationRequest(alice, read, report, policies,
    ///     context: new { trusted = true },
    ///     entities: new[] { new CedarEntity(report, new { owner = CedarValue.Entity(alice) }) });
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Serializing arbitrary CLR context uses reflection. Use the JsonElement context overload for trim/AOT.")]
    [RequiresDynamicCode("Serializing arbitrary CLR context uses reflection. Use the JsonElement context overload for trim/AOT.")]
    public CedarAuthorizationRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        CedarPolicySet policies, object? context, IEnumerable<CedarEntity>? entities,
        CedarSchema? schema = null, bool validateRequest = true)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(policies);
        Principal = principal; Action = action; Resource = resource; Policies = policies;
        var ctx = JsonWire.FromObjectReflection(context ?? new { });
        if (ctx.ValueKind != JsonValueKind.Object) throw new ArgumentException("Context must serialize to a JSON Object.", nameof(context));
        Context = ctx;
        Entities = WriteEntities(entities);
        Schema = schema; ValidateRequest = validateRequest;
    }
    /// <summary>Creates a request from typed entities and a copied JSON context object. Trim/AOT-safe.</summary>
    public CedarAuthorizationRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        CedarPolicySet policies, JsonElement context, IEnumerable<CedarEntity>? entities,
        CedarSchema? schema = null, bool validateRequest = true)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(policies);
        if (context.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(context));
        Principal = principal; Action = action; Resource = resource; Policies = policies;
        Context = context.Clone();
        Entities = WriteEntities(entities);
        Schema = schema; ValidateRequest = validateRequest;
    }
    private static JsonElement WriteEntities(IEnumerable<CedarEntity>? entities) => JsonWire.Build(w => {
        w.WriteStartArray();
        foreach (var e in entities ?? Enumerable.Empty<CedarEntity>())
        {
            ArgumentNullException.ThrowIfNull(e, nameof(entities));
            e.Write(w);
        }
        w.WriteEndArray();
    });
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
