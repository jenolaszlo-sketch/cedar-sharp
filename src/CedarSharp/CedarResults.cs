using System.Text.Json;

namespace CedarSharp;

public enum CedarDecision { Deny, Allow }

/// <summary>Source offsets are UTF-8 byte offsets, with an exclusive end.</summary>
public sealed record CedarSourceLocation(string? Label, long Start, long End);

/// <summary>Complete structured upstream diagnostic, including related diagnostics and source ranges.</summary>
public sealed class CedarDiagnostic
{
    public string Message { get; }
    public string? Help { get; }
    public string? Code { get; }
    public string? Url { get; }
    public string? Severity { get; }
    public IReadOnlyList<CedarSourceLocation> SourceLocations { get; }
    public IReadOnlyList<CedarDiagnostic> Related { get; }
    public JsonElement Raw { get; }
    internal CedarDiagnostic(JsonElement e)
    {
        Raw = e.Clone(); Message = JsonWire.String(e, "message");
        Help = e.GetProperty("help").GetString(); Code = e.GetProperty("code").GetString();
        Url = e.GetProperty("url").GetString(); Severity = e.GetProperty("severity").GetString();
        SourceLocations = JsonWire.Array(e, "sourceLocations", x => new CedarSourceLocation(
            x.GetProperty("label").GetString(), x.GetProperty("start").GetInt64(), x.GetProperty("end").GetInt64()));
        Related = JsonWire.Array(e, "related", x => new CedarDiagnostic(x));
    }
}

public sealed record CedarPolicyDiagnostic(string PolicyId, CedarDiagnostic Error)
{
    internal static CedarPolicyDiagnostic Read(JsonElement e) => new(JsonWire.String(e, "policyId"), new(e.GetProperty("error")));
}

/// <summary>A completed Cedar call. A parsing failure has no decision; an Allow may still have policy errors.</summary>
public sealed class CedarAuthorizationResult
{
    public bool IsSuccess { get; }
    public CedarDecision? Decision { get; }
    public IReadOnlyList<string> DeterminingPolicies { get; } = System.Array.Empty<string>();
    public IReadOnlyList<CedarPolicyDiagnostic> PolicyErrors { get; } = System.Array.Empty<CedarPolicyDiagnostic>();
    public IReadOnlyList<CedarDiagnostic> Errors { get; } = System.Array.Empty<CedarDiagnostic>();
    public IReadOnlyList<CedarDiagnostic> Warnings { get; }
    public JsonElement Raw { get; }
    internal CedarAuthorizationResult(JsonElement e)
    {
        Raw = e.Clone(); IsSuccess = JsonWire.Success(e);
        Warnings = JsonWire.Array(e, "warnings", x => new CedarDiagnostic(x));
        if (!IsSuccess) { Errors = JsonWire.Array(e, "errors", x => new CedarDiagnostic(x)); return; }
        var response = e.GetProperty("response");
        Decision = JsonWire.String(response, "decision") switch
        {
            "allow" => CedarDecision.Allow, "deny" => CedarDecision.Deny,
            _ => throw new JsonException("Unknown Cedar decision.")
        };
        var diagnostics = response.GetProperty("diagnostics");
        DeterminingPolicies = JsonWire.Array(diagnostics, "reason", x => x.GetString() ?? throw new JsonException("Null policy ID."));
        PolicyErrors = JsonWire.Array(diagnostics, "errors", CedarPolicyDiagnostic.Read);
    }
}

/// <summary>Separates a failed parse from a completed validation that found invalid policies.</summary>
public sealed class CedarValidationResult
{
    public bool IsSuccess { get; }
    public bool IsValid => IsSuccess && ValidationErrors.Count == 0;
    public IReadOnlyList<CedarPolicyDiagnostic> ValidationErrors { get; } = System.Array.Empty<CedarPolicyDiagnostic>();
    public IReadOnlyList<CedarPolicyDiagnostic> ValidationWarnings { get; } = System.Array.Empty<CedarPolicyDiagnostic>();
    public IReadOnlyList<CedarDiagnostic> Errors { get; } = System.Array.Empty<CedarDiagnostic>();
    public IReadOnlyList<CedarDiagnostic> Warnings { get; }
    public JsonElement Raw { get; }
    internal CedarValidationResult(JsonElement e)
    {
        Raw = e.Clone(); IsSuccess = JsonWire.Success(e);
        Warnings = JsonWire.Array(e, IsSuccess ? "otherWarnings" : "warnings", x => new CedarDiagnostic(x));
        if (!IsSuccess) { Errors = JsonWire.Array(e, "errors", x => new CedarDiagnostic(x)); return; }
        ValidationErrors = JsonWire.Array(e, "validationErrors", CedarPolicyDiagnostic.Read);
        ValidationWarnings = JsonWire.Array(e, "validationWarnings", CedarPolicyDiagnostic.Read);
    }
}

/// <summary>The result of a Cedar parsing or data-validation check.</summary>
public sealed class CedarCheckResult
{
    public bool IsSuccess { get; }
    public IReadOnlyList<CedarDiagnostic> Errors { get; }
    public JsonElement Raw { get; }
    internal CedarCheckResult(JsonElement e)
    {
        Raw = e.Clone(); IsSuccess = JsonWire.Success(e);
        Errors = IsSuccess ? System.Array.Empty<CedarDiagnostic>() : JsonWire.Array(e, "errors", x => new CedarDiagnostic(x));
    }
}

/// <summary>Identity reported by the loaded bridge, together with its verified asset path and hash.</summary>
public sealed record CedarVersion(uint AbiVersion, string SdkVersion, string LanguageVersion, string BridgeVersion,
    string RustVersion, string Target, IReadOnlyList<string> Features, string NativePath, string Sha256);

/// <summary>A native loading, ABI, transport, or result-decoding failure. Never represents a Cedar Deny.</summary>
public sealed class CedarBridgeException : Exception
{
    public uint? Status { get; }
    public CedarBridgeException(string message, uint? status = null, Exception? innerException = null) : base(message, innerException) => Status = status;
}