using CedarSharp;

var engine = new CedarEngine();
var request = new CedarAuthorizationRequest(
    new CedarEntityUid("User", "alice"),
    new CedarEntityUid("Action", "read"),
    new CedarEntityUid("Document", "report"),
    CedarPolicySet.FromText("permit(principal, action, resource);"));
var result = engine.Authorize(request);
if (!result.IsSuccess)
{
    throw new InvalidOperationException("Cedar authorization returned a bridge failure.");
}
if (result.Decision != CedarDecision.Allow)
{
    throw new InvalidOperationException($"Expected Allow, received {result.Decision}.");
}
if (result.PolicyErrors.Count != 0)
{
    throw new InvalidOperationException($"Expected no policy diagnostics, received {result.PolicyErrors.Count}.");
}
var version = engine.GetVersion();
if (version.AbiVersion != 1 || version.BridgeVersion != "0.1.0" || version.LanguageVersion != "4.5" || version.RustVersion != "1.94.0")
{
    throw new InvalidOperationException("Loaded native identity does not match CedarSharp's pinned baseline.");
}
var expectedTarget = Environment.GetEnvironmentVariable("CEDARSHARP_EXPECT_TARGET");
if (!string.IsNullOrEmpty(expectedTarget) && version.Target != expectedTarget)
{
    throw new InvalidOperationException($"Expected native target {expectedTarget}, received {version.Target}.");
}
if (version.SdkVersion != "4.13.0")
{
    throw new InvalidOperationException($"Expected Cedar 4.13.0, received {version.SdkVersion}.");
}
Console.WriteLine($"CedarSharp packaged consumer passed: {result.Decision}; Cedar {version.SdkVersion}.");
