using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CedarSharp;

// The numeric values are the stable ABI 1 operation IDs in docs/native-boundary.md.
internal enum CedarOperation : uint
{
    Version = 0,
    Authorize = 1,
    ValidatePolicies = 2,
    CheckPolicies = 3,
    CheckSchema = 4,
    CheckEntities = 5,
    CheckContext = 6,
    CheckRequest = 7
}

internal sealed unsafe class NativeBridge
{
    internal const uint ExpectedAbi = 1;
    internal const string ExpectedSdk = "4.13.0";
    internal const string ExpectedBridge = "0.1.0";
    internal const string ExpectedRust = "1.94.0";
    internal const string ExpectedLanguage = "4.5";
    internal static readonly string[] ExpectedFeatures = ["datetime", "decimal", "ipaddr"];

    private static readonly object Sync = new();
    private static NativeBridge? cached;
    internal static NativeBridge Instance
    {
        get
        {
            // Cache only success so a fixed staging directory can be retried in the same process.
            lock (Sync) { return cached ??= new NativeBridge(); }
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Buffer { internal byte* Data; internal nuint Length; }
    private readonly delegate* unmanaged[Cdecl]<uint, byte*, nuint, Buffer*, uint> call;
    private readonly delegate* unmanaged[Cdecl]<Buffer, void> free;
    // Intentionally retain the library for process lifetime: callers may run concurrently.
    private readonly nint library;
    internal CedarVersion Version { get; }

    private NativeBridge()
    {
        var (rid, file, target) = Platform();
        var path = ResolvePath(rid, file);
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing {file} for {rid}. Build native assets (pwsh ./eng/Build-Native.ps1) or install a CedarSharp package containing this RID.", path);
            var manifestDir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(manifestDir)) manifestDir = AppContext.BaseDirectory;
            using var manifestDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(manifestDir, "cedarsharp-native.json")), JsonWire.DocumentOptions);
            var manifest = manifestDoc.RootElement;
            string hash;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                hash = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            if (!string.Equals(hash, JsonWire.OptionalString(manifest, "sha256"), StringComparison.OrdinalIgnoreCase))
                throw new CedarBridgeException("CedarSharp native asset hash does not match its manifest.");
            var mismatch =
                JsonWire.OptionalString(manifest, "rid") != rid ? $"rid (expected {rid})" :
                JsonWire.OptionalString(manifest, "target") != target ? $"target (expected {target})" :
                (!manifest.TryGetProperty("abiVersion", out var abi) || abi.GetUInt32() != ExpectedAbi) ? "abiVersion" :
                JsonWire.OptionalString(manifest, "sdkVersion") != ExpectedSdk ? "sdkVersion" :
                JsonWire.OptionalString(manifest, "bridgeVersion") != ExpectedBridge ? "bridgeVersion" :
                JsonWire.OptionalString(manifest, "rustVersion") != ExpectedRust ? "rustVersion" : null;
            if (mismatch is not null)
                throw new CedarBridgeException($"Incompatible CedarSharp native asset manifest: {mismatch}.");
            var manifestFeatures = JsonWire.Array(manifest, "features", x => x.GetString() ?? throw new JsonException("Null feature."));
            if (!manifestFeatures.Order(StringComparer.Ordinal).SequenceEqual(ExpectedFeatures))
                throw new CedarBridgeException("Incompatible CedarSharp native feature manifest.");
            library = NativeLibrary.Load(Path.GetFullPath(path));
            try
            {
                var abiFn = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(library, "cedarsharp_abi_version");
                if (abiFn() != ExpectedAbi) throw new CedarBridgeException($"Incompatible CedarSharp native ABI; expected version {ExpectedAbi}.");
                call = (delegate* unmanaged[Cdecl]<uint, byte*, nuint, Buffer*, uint>)NativeLibrary.GetExport(library, "cedarsharp_call_v1");
                free = (delegate* unmanaged[Cdecl]<Buffer, void>)NativeLibrary.GetExport(library, "cedarsharp_free_v1");
                var version = Call(CedarOperation.Version, "{}"u8.ToArray());
                var features = JsonWire.Array(version, "features", x => x.GetString() ?? throw new JsonException("Null feature."));
                Version = new(version.GetProperty("abiVersion").GetUInt32(), JsonWire.String(version, "sdkVersion"),
                    JsonWire.String(version, "languageVersion"), JsonWire.String(version, "bridgeVersion"),
                    JsonWire.String(version, "rustVersion"), JsonWire.String(version, "target"), features, Path.GetFullPath(path), hash);
                if (Version.AbiVersion != ExpectedAbi || Version.SdkVersion != ExpectedSdk || Version.BridgeVersion != ExpectedBridge ||
                    Version.RustVersion != ExpectedRust || Version.LanguageVersion != ExpectedLanguage || Version.Target != target ||
                    !features.Order(StringComparer.Ordinal).SequenceEqual(ExpectedFeatures))
                    throw new CedarBridgeException("Loaded Cedar engine identity differs from the required baseline.");
            }
            catch { NativeLibrary.Free(library); throw; }
        }
        catch (CedarBridgeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or
            EntryPointNotFoundException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CedarBridgeException($"Could not load verified CedarSharp native runtime for {rid} from '{path}': {ex.Message}", innerException: ex); }
    }

    private static string ResolvePath(string rid, string file)
    {
        // Explicit override for self-built or vendored natives. The asset is still
        // hash- and identity-verified; the override only selects the location.
        // Relative paths are normalized against the current directory so the
        // adjacent manifest is resolved beside the chosen library.
        var overridePath = Environment.GetEnvironmentVariable("CEDARSHARP_NATIVE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var full = Path.GetFullPath(overridePath);
            if (Directory.Exists(full))
            {
                var nested = Path.Combine(full, "runtimes", rid, "native", file);
                if (File.Exists(nested)) return Path.GetFullPath(nested);
                var flat = Path.Combine(full, file);
                if (File.Exists(flat)) return Path.GetFullPath(flat);
                return Path.GetFullPath(nested);
            }
            return full;
        }
        // AppContext.BaseDirectory is the deployed application directory and is
        // single-file/NativeAOT friendly, unlike Assembly.Location.
        var directory = AppContext.BaseDirectory;
        var nestedPath = Path.Combine(directory, "runtimes", rid, "native", file);
        var flatPath = Path.Combine(directory, file);
        return Path.GetFullPath(File.Exists(nestedPath) ? nestedPath : flatPath);
    }

    internal JsonElement Call(CedarOperation operation, byte[] input)
    {
        if (input.Length > JsonWire.MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.", nameof(input));
        Buffer output = default;
        try
        {
            uint status;
            fixed (byte* data = input) status = call((uint)operation, data, (nuint)input.Length, &output);
            if (output.Data == null || output.Length == 0 || output.Length > 64 * 1024 * 1024)
                throw new CedarBridgeException("Native bridge returned an invalid output buffer.", status);
            var span = new ReadOnlySpan<byte>(output.Data, checked((int)output.Length));
            using var doc = JsonDocument.Parse(span.ToArray(), JsonWire.DocumentOptions);
            if (status == 1)
                throw new CedarInputException($"Cedar JSON input was rejected: {JsonWire.OptionalString(doc.RootElement, "message") ?? "unknown input error"}");
            if (status != 0)
                throw new CedarBridgeException($"Native Cedar bridge failed: {JsonWire.OptionalString(doc.RootElement, "message") ?? "unknown bridge error"}", status);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CedarBridgeException("Native bridge returned an invalid response.", innerException: ex); }
        finally { if (output.Data != null) free(output); }
    }

    private static (string Rid, string File, string Target) Platform()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsWindows() && arch == Architecture.X64)
            return ("win-x64", "cedarsharp_native.dll", "x86_64-pc-windows-msvc");
        if (OperatingSystem.IsWindows() && arch == Architecture.Arm64)
            return ("win-arm64", "cedarsharp_native.dll", "aarch64-pc-windows-msvc");
        if (OperatingSystem.IsLinux() && arch == Architecture.X64 && !IsMusl())
            return ("linux-x64", "libcedarsharp_native.so", "x86_64-unknown-linux-gnu");
        if (OperatingSystem.IsLinux() && arch == Architecture.Arm64 && !IsMusl())
            return ("linux-arm64", "libcedarsharp_native.so", "aarch64-unknown-linux-gnu");
        if (OperatingSystem.IsMacOS() && arch == Architecture.Arm64)
            return ("osx-arm64", "libcedarsharp_native.dylib", "aarch64-apple-darwin");
        if (OperatingSystem.IsMacOS() && arch == Architecture.X64)
            return ("osx-x64", "libcedarsharp_native.dylib", "x86_64-apple-darwin");
        throw new PlatformNotSupportedException(
            $"CedarSharp has no native runtime for {RuntimeInformation.RuntimeIdentifier}/{arch}. " +
            "Supported staged RIDs are win-x64, linux-x64 and osx-arm64; win-arm64, linux-arm64 and osx-x64 resolve but require a self-built asset " +
            "(pwsh ./eng/Build-Native.ps1) or CEDARSHARP_NATIVE_PATH pointing at a verified build.");
    }

    private static bool IsMusl() =>
        RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal);
}
