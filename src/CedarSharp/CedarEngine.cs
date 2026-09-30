using System.Text.Json;

namespace CedarSharp;

/// <summary>Synchronous, stateless access to the official Cedar engine. Instances and immutable inputs may be shared across threads.</summary>
public sealed class CedarEngine
{
    /// <summary>Returns the identity of the loaded and verified native Cedar engine.</summary>
    public CedarVersion GetVersion() => NativeBridge.Instance.Version;

    /// <summary>Evaluates one request without implicitly validating its policies against a schema.</summary>
    /// <remarks>A successful call (<c>IsSuccess</c>) means Cedar evaluated the request, not that access was allowed.
    /// Use <see cref="CedarAuthorizationResult.IsCleanAllow"/> or <see cref="CedarAuthorizationResult.RequireAllow"/> for fail-closed enforcement.</remarks>
    /// <exception cref="CedarBridgeException">Native loading, ABI, or transport failure. Never a Cedar Deny.</exception>
    /// <exception cref="CedarInputException">The call envelope was rejected before evaluation.</exception>
    public CedarAuthorizationResult Authorize(CedarAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = CedarSharpDiagnostics.Start("cedarsharp.authorize");
        if (CedarSharpDiagnostics.IncludeIdentity)
        {
            activity?.SetTag("cedar.principal", request.Principal.ToString());
            activity?.SetTag("cedar.action", request.Action.ToString());
            activity?.SetTag("cedar.resource", request.Resource.ToString());
        }
        activity?.SetTag("cedar.validate_request", request.Schema is not null && request.ValidateRequest);
        try
        {
            var result = Decode(CedarOperation.Authorize, request.Write, e => new CedarAuthorizationResult(e));
            CedarSharpDiagnostics.RecordVersion(activity);
            activity?.SetTag("cedar.decision", result.Decision?.ToString());
            activity?.SetTag("cedar.is_success", result.IsSuccess);
            activity?.SetTag("cedar.policy_error_count", result.PolicyErrors.Count);
            activity?.SetTag("cedar.determining_policy_count", result.DeterminingPolicies.Count);
            return result;
        }
        catch (Exception ex) { CedarSharpDiagnostics.RecordFailure(activity, ex); throw; }
    }
    /// <summary>Convenience overload that builds a request from scope identifiers and JSON context/entities.</summary>
    public CedarAuthorizationResult Authorize(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        CedarPolicySet policies, string contextJson = "{}", string entitiesJson = "[]",
        CedarSchema? schema = null, bool validateRequest = true)
        => Authorize(new CedarAuthorizationRequest(principal, action, resource, policies, contextJson, entitiesJson, schema, validateRequest));
    /// <summary>Evaluates requests, preserving input order. Set <paramref name="parallel"/> to evaluate on the thread pool.</summary>
    /// <param name="requests">Requests to evaluate. The sequence is snapshotted before evaluation.</param>
    /// <param name="parallel">Run evaluations concurrently. A native call cannot be interrupted once started.</param>
    /// <param name="maxDegreeOfParallelism">Concurrency cap when <paramref name="parallel"/> is set; null uses the thread pool default.</param>
    /// <exception cref="AggregateException">With <paramref name="parallel"/>, wraps failures from one or more evaluations.</exception>
    public IReadOnlyList<CedarAuthorizationResult> AuthorizeBatch(IEnumerable<CedarAuthorizationRequest> requests,
        bool parallel = false, int? maxDegreeOfParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (maxDegreeOfParallelism is <= 0) throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
        var list = requests.ToArray();
        foreach (var request in list) ArgumentNullException.ThrowIfNull(request, nameof(requests));
        var results = new CedarAuthorizationResult[list.Length];
        if (parallel)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism ?? -1 };
            Parallel.For(0, list.Length, options, i => results[i] = Authorize(list[i]));
        }
        else for (var i = 0; i < list.Length; i++) results[i] = Authorize(list[i]);
        return Array.AsReadOnly(results);
    }
    /// <summary>Validates policies against a schema in Cedar strict mode.</summary>
    public CedarValidationResult ValidatePolicies(CedarPolicySet policies, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(policies); ArgumentNullException.ThrowIfNull(schema);
        using var activity = CedarSharpDiagnostics.Start("cedarsharp.validate_policies");
        try
        {
            var result = Decode(CedarOperation.ValidatePolicies, w => {
                w.WriteStartObject(); w.WritePropertyName("policies"); policies.Value.WriteTo(w);
                w.WritePropertyName("schema"); schema.Value.WriteTo(w); w.WriteEndObject();
            }, e => new CedarValidationResult(e));
            CedarSharpDiagnostics.RecordVersion(activity);
            activity?.SetTag("cedar.is_success", result.IsSuccess);
            activity?.SetTag("cedar.is_valid", result.IsValid);
            activity?.SetTag("cedar.validation_error_count", result.ValidationErrors.Count);
            activity?.SetTag("cedar.validation_warning_count", result.ValidationWarnings.Count);
            return result;
        }
        catch (Exception ex) { CedarSharpDiagnostics.RecordFailure(activity, ex); throw; }
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
        return CheckEntitiesElement(entities, schema);
    }
    /// <summary>Parses already-built entities and, when supplied, checks them against a schema.</summary>
    public CedarCheckResult CheckEntities(IEnumerable<CedarEntity> entities, CedarSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return Decode(CedarOperation.CheckEntities, w => {
            w.WriteStartObject(); w.WriteStartArray("entities");
            foreach (var e in entities) e.Write(w);
            w.WriteEndArray();
            if (schema is not null) { w.WritePropertyName("schema"); schema.Value.WriteTo(w); }
            w.WriteEndObject();
        }, e => new CedarCheckResult(e));
    }
    private CedarCheckResult CheckEntitiesElement(JsonElement entities, CedarSchema? schema) => Decode(CedarOperation.CheckEntities, w => {
        w.WriteStartObject(); w.WritePropertyName("entities"); entities.WriteTo(w);
        if (schema is not null) { w.WritePropertyName("schema"); schema.Value.WriteTo(w); }
        w.WriteEndObject();
    }, e => new CedarCheckResult(e));
    /// <summary>Parses context. Schema-based validation requires both schema and action.</summary>
    public CedarCheckResult CheckContext(string contextJson, CedarSchema? schema = null, CedarEntityUid? action = null)
    {
        if ((schema is null) != (action is null)) throw new ArgumentException("Supply both schema and action for context validation.");
        var context = JsonWire.Parse(contextJson, JsonValueKind.Object);
        return CheckContextElement(context, schema, action);
    }
    /// <summary>Validates a serializable context object. Schema-based validation requires both schema and action. Uses reflection; prefer the <see cref="JsonElement"/> overload for trim/AOT.</summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Serializing arbitrary CLR context uses reflection. Use the JsonElement overload for trim/AOT.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Serializing arbitrary CLR context uses reflection. Use the JsonElement overload for trim/AOT.")]
    public CedarCheckResult CheckContext<T>(T? context, CedarSchema? schema = null, CedarEntityUid? action = null)
        => CheckContext(JsonWire.FromObjectReflection(context), schema, action);
    /// <summary>Validates a copied JSON context object. Schema-based validation requires both schema and action. Trim/AOT-safe.</summary>
    public CedarCheckResult CheckContext(JsonElement context, CedarSchema? schema = null, CedarEntityUid? action = null)
    {
        if ((schema is null) != (action is null)) throw new ArgumentException("Supply both schema and action for context validation.");
        if (context.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(context));
        return CheckContextElement(context, schema, action);
    }
    private CedarCheckResult CheckContextElement(JsonElement context, CedarSchema? schema, CedarEntityUid? action) => Decode(CedarOperation.CheckContext, w => {
        w.WriteStartObject(); w.WritePropertyName("context"); context.WriteTo(w);
        if (schema is not null) { w.WritePropertyName("schema"); schema.Value.WriteTo(w); }
        if (action is not null) { w.WritePropertyName("action"); action.Write(w); }
        w.WriteEndObject();
    }, e => new CedarCheckResult(e));
    /// <summary>Validates principal, action and resource against a schema; check context and entities separately, or use <see cref="CheckFullRequest(CedarSharp.CedarAuthorizationRequest,CedarSharp.CedarSchema)"/>.</summary>
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
    /// <summary>Validates scope variables (principal, action, resource) against a schema. Alias of <see cref="CheckRequest"/> matching upstream's naming.</summary>
    public CedarCheckResult CheckScope(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource, CedarSchema schema)
        => CheckRequest(principal, action, resource, schema);
    /// <summary>Validates scope, context, and entities together against a schema. Success requires all three to pass.</summary>
    /// <example>
    /// <code>
    /// var check = engine.CheckFullRequest(alice, read, report, request.Context, request.Entities, schema);
    /// if (!check.IsSuccess) return Results.BadRequest(check.ToString());
    /// </code>
    /// </example>
    public CedarFullRequestCheckResult CheckFullRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        JsonElement context, JsonElement entities, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(principal); ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(resource); ArgumentNullException.ThrowIfNull(schema);
        if (context.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON Object.", nameof(context));
        if (entities.ValueKind != JsonValueKind.Array) throw new ArgumentException("Expected a JSON Array.", nameof(entities));
        var scope = CheckRequest(principal, action, resource, schema);
        var ctx = CheckContextElement(context, schema, action);
        var ents = CheckEntitiesElement(entities, schema);
        return new CedarFullRequestCheckResult(scope, ctx, ents);
    }
    /// <summary>Validates scope, context JSON, and entities JSON together against a schema.</summary>
    public CedarFullRequestCheckResult CheckFullRequest(CedarEntityUid principal, CedarEntityUid action, CedarEntityUid resource,
        string contextJson, string entitiesJson, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return CheckFullRequest(principal, action, resource,
            JsonWire.Parse(contextJson, JsonValueKind.Object),
            JsonWire.Parse(entitiesJson, JsonValueKind.Array), schema);
    }
    /// <summary>Validates the scope, context, and entities carried by an authorization request.</summary>
    public CedarFullRequestCheckResult CheckFullRequest(CedarAuthorizationRequest request, CedarSchema schema)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(schema);
        return CheckFullRequest(request.Principal, request.Action, request.Resource, request.Context, request.Entities, schema);
    }
    /// <summary>Validates a request against the schema it carries.</summary>
    /// <exception cref="ArgumentException">The request has no schema.</exception>
    public CedarFullRequestCheckResult CheckFullRequest(CedarAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Schema is null) throw new ArgumentException("The request has no schema. Supply a schema explicitly.", nameof(request));
        return CheckFullRequest(request, request.Schema);
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
