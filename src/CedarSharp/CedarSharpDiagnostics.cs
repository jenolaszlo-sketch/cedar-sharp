using System.Diagnostics;

namespace CedarSharp;

/// <summary>Instrumentation hooks for OpenTelemetry and <see cref="ActivityListener"/> consumers.</summary>
/// <remarks>Activities are created around authorization and validation calls. When no listener is subscribed,
/// <c>ActivitySource.StartActivity</c> returns null and overhead is negligible. By default activities carry only
/// outcome metadata (decision, error counts, engine/bridge versions) and never principal/action/resource identities
/// or diagnostic text. Set <see cref="IncludeIdentity"/> to opt in to identity tags for trusted, access-controlled sinks.</remarks>
/// <example>
/// <code>
/// using var listener = new ActivityListener
/// {
///     ShouldListenTo = s => s.Name == CedarSharpDiagnostics.ActivitySourceName,
///     Sample = (ref ActivityCreationOptions&lt;ActivityContext&gt; _) => ActivitySamplingResult.AllDataAndRecorded,
///     ActivityStarted = a => Console.WriteLine($"start {a.DisplayName}")
/// };
/// ActivitySource.AddActivityListener(listener);
/// </code>
/// </example>
public static class CedarSharpDiagnostics
{
    /// <summary>Name of the emitted <see cref="ActivitySource"/>: <c>CedarSharp</c>.</summary>
    public const string ActivitySourceName = "CedarSharp";

    /// <summary>Source for per-call authorization and validation activities. Subscribe with OpenTelemetry or an <see cref="ActivityListener"/>.</summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName, typeof(CedarSharpDiagnostics).Assembly.GetName().Version?.ToString(3));

    /// <summary>When true, adds principal/action/resource UID tags to authorization activities. Default false to avoid exporting identity by default.</summary>
    public static bool IncludeIdentity { get; set; }

    /// <summary>When true, records exception messages and failure descriptions on activities. Default false to avoid exporting policy/error text by default.</summary>
    public static bool IncludeErrorDetails { get; set; }

    internal static Activity? Start(string name) => ActivitySource.StartActivity(name);

    internal static void RecordVersion(Activity? activity)
    {
        if (activity is null) return;
        var version = NativeBridge.Instance.Version;
        activity.SetTag("cedar.engine.version", version.SdkVersion);
        activity.SetTag("cedar.bridge.version", version.BridgeVersion);
        activity.SetTag("cedar.abi.version", version.AbiVersion);
        activity.SetTag("cedar.target", version.Target);
    }

    internal static void RecordFailure(Activity? activity, Exception exception)
    {
        if (activity is null) return;
        activity.SetStatus(ActivityStatusCode.Error, IncludeErrorDetails ? exception.Message : null);
        if (activity.IsAllDataRequested)
        {
            var tags = new ActivityTagsCollection { ["exception.type"] = exception.GetType().FullName };
            if (IncludeErrorDetails) tags["exception.message"] = exception.Message;
            activity.AddEvent(new ActivityEvent("exception", tags: tags));
        }
    }
}
