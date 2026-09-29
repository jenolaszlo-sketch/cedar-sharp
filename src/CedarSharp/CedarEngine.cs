using System.Text.Json;

namespace CedarSharp;

/// <summary>Synchronous, stateless access to the official Cedar engine. Instances and immutable inputs may be shared across threads.</summary>
public sealed class CedarEngine
{
    /// <summary>Returns the identity of the loaded and verified native Cedar engine.</summary>
    public CedarVersion GetVersion() => NativeBridge.Instance.Version;

    /// <summary>Evaluates one request without implicitly validating its policies against a schema.</summary>
    public CedarAuthorizationResult Authorize(CedarAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Decode(CedarOperation.Authorize, request.Write, e => new CedarAuthorizationResult(e));
    }
    /// <summary>Validates policies against a schema in Cedar strict mode.</summary>
    public CedarValidationResult ValidatePolicies(CedarPolicySet policies, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(policies); ArgumentNullException.ThrowIfNull(schema);
        return Decode(CedarOperation.ValidatePolicies, w => {
            w.WriteStartObject(); w.WritePropertyName("policies"); policies.Value.WriteTo(w);
            w.WritePropertyName("schema"); schema.Value.WriteTo(w); w.WriteEndObject();
        }, e => new CedarValidationResult(e));
    }
    /// <summary>Checks whether a policy set parses; this does not perform schema validation.</summary>
    public CedarCheckResult CheckPolicies(CedarPolicySet policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        return Decode(CedarOperation.CheckPolicies, policies.Value.WriteTo, e => new CedarCheckResult(e));
    }
    /// <summary>Checks whether a Cedar text or JSON schema parses.</summary>
    public CedarCheckResult CheckSchema(CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return Decode(CedarOperation.CheckSchema, schema.Value.WriteTo, e => new CedarCheckResult(e));
    }
    /// <summary>Parses entities and, when supplied, checks them against a schema.</summary>
    public CedarCheckResult CheckEntities(string entitiesJson, CedarSchema? schema = null)
    {
        var entities = JsonWire.Parse(entitiesJson, JsonValueKind.Array);
        return Decode(CedarOperation.CheckEntities, w => {
            w.WriteStartObject(); w.WritePropertyName("entities"); entities.WriteTo(w);
            if (schema is not null) { w.WritePropertyName("schema"); schema.Value.WriteTo(w); }
            w.WriteEndObject();
        }, e => new CedarCheckResult(e));
    }
    /// <summary>Parses context. Schema-based validation requires both schema and action.</summary>
    public CedarCheckResult CheckContext(string contextJson, CedarSchema? schema = null, CedarEntityUid? action = null)
    {
        if ((schema is null) != (action is null)) throw new ArgumentException("Supply both schema and action for context validation.");
        var context = JsonWire.Parse(contextJson, JsonValueKind.Object);
        return Decode(CedarOperation.CheckContext, w => {
            w.WriteStartObject(); w.WritePropertyName("context"); context.WriteTo(w);
            if (schema is not null) { w.WritePropertyName("schema"); schema.Value.WriteTo(w); }
            if (action is not null) { w.WritePropertyName("action"); action.Write(w); }
            w.WriteEndObject();
        }, e => new CedarCheckResult(e));
    }
    /// <summary>Validates principal, action and resource against a schema; check context and entities separately.</summary>
    public CedarCheckResult CheckRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(principal); ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(schema);
        return Decode(CedarOperation.CheckRequest, w => {
            w.WriteStartObject(); w.WritePropertyName("principal"); principal.Write(w);
            w.WritePropertyName("action"); action.Write(w); w.WritePropertyName("resource"); resource.Write(w);
            w.WritePropertyName("schema"); schema.Value.WriteTo(w); w.WriteEndObject();
        }, e => new CedarCheckResult(e));
    }
    private static T Decode<T>(CedarOperation operation, Action<Utf8JsonWriter> write, Func<JsonElement, T> read)
    {
        var bytes = JsonWire.Encode(write);
        var answer = NativeBridge.Instance.Call(operation, bytes);
        try { return read(answer); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new CedarBridgeException("The native Cedar response did not match the expected contract.", innerException: ex); }
    }
}