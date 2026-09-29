using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace CedarSharp;

internal sealed unsafe class NativeBridge
{
    private static readonly Lazy<NativeBridge> Loaded = new(() => new NativeBridge());
    internal static NativeBridge Instance => Loaded.Value;
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
        var directory = Path.GetDirectoryName(typeof(NativeBridge).Assembly.Location);
        if (string.IsNullOrEmpty(directory)) directory = AppContext.BaseDirectory;
        var nested = Path.Combine(directory, "runtimes", rid, "native", file);
        var flat = Path.Combine(directory, file);
        var path = File.Exists(nested) ? nested : flat;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Missing {file} for {rid}. Build native assets or install a CedarSharp package containing this RID.", path);
            using var manifestDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "cedarsharp-native.json")));
            var manifest = manifestDoc.RootElement;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!string.Equals(hash, JsonWire.String(manifest, "sha256"), StringComparison.OrdinalIgnoreCase))
                throw new CedarBridgeException("CedarSharp native asset hash does not match its manifest.");
            if (JsonWire.String(manifest, "rid") != rid || JsonWire.String(manifest, "target") != target ||
                manifest.GetProperty("abiVersion").GetUInt32() != 1 || JsonWire.String(manifest, "sdkVersion") != "4.13.0" ||
                JsonWire.String(manifest, "bridgeVersion") != "0.1.0" || JsonWire.String(manifest, "rustVersion") != "1.94.0")
                throw new CedarBridgeException("Incompatible CedarSharp native asset manifest.");
            var manifestFeatures = JsonWire.Array(manifest, "features", x => x.GetString() ?? throw new JsonException("Null feature."));
            if (!manifestFeatures.Order(StringComparer.Ordinal).SequenceEqual(new[] { "datetime", "decimal", "ipaddr" }))
                throw new CedarBridgeException("Incompatible CedarSharp native feature manifest.");
            library = NativeLibrary.Load(Path.GetFullPath(path));
            try
            {
                var abi = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(library, "cedarsharp_abi_version");
                if (abi() != 1) throw new CedarBridgeException("Incompatible CedarSharp native ABI; expected version 1.");
                call = (delegate* unmanaged[Cdecl]<uint, byte*, nuint, Buffer*, uint>)NativeLibrary.GetExport(library, "cedarsharp_call_v1");
                free = (delegate* unmanaged[Cdecl]<Buffer, void>)NativeLibrary.GetExport(library, "cedarsharp_free_v1");
                var version = Call(0, "{}"u8.ToArray());
                var features = JsonWire.Array(version, "features", x => x.GetString() ?? throw new JsonException("Null feature."));
                Version = new(version.GetProperty("abiVersion").GetUInt32(), JsonWire.String(version, "sdkVersion"),
                    JsonWire.String(version, "languageVersion"), JsonWire.String(version, "bridgeVersion"),
                    JsonWire.String(version, "rustVersion"), JsonWire.String(version, "target"), features, Path.GetFullPath(path), hash);
                if (Version.AbiVersion != 1 || Version.SdkVersion != "4.13.0" || Version.BridgeVersion != "0.1.0" ||
                    Version.RustVersion != "1.94.0" || Version.LanguageVersion != "4.5" || Version.Target != target ||
                    !features.Order(StringComparer.Ordinal).SequenceEqual(new[] { "datetime", "decimal", "ipaddr" }))
                    throw new CedarBridgeException("Loaded Cedar engine identity differs from the required baseline.");
            }
            catch { NativeLibrary.Free(library); throw; }
        }
        catch (CedarBridgeException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or
            EntryPointNotFoundException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CedarBridgeException($"Could not load verified CedarSharp native runtime for {rid} from '{path}': {ex.Message}", innerException: ex); }
    }

    internal JsonElement Call(uint operation, byte[] input)
    {
        if (input.Length > JsonWire.MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.", nameof(input));
        Buffer output = default;
        try
        {
            uint status;
            fixed (byte* data = input) status = call(operation, data, (nuint)input.Length, &output);
            if (output.Data == null || output.Length == 0 || output.Length > 64 * 1024 * 1024)
                throw new CedarBridgeException("Native bridge returned an invalid output buffer.", status);
            using var doc = JsonDocument.Parse(new ReadOnlySpan<byte>(output.Data, checked((int)output.Length)).ToArray(), new JsonDocumentOptions { MaxDepth = 256 });
            if (status != 0)
                throw new CedarBridgeException($"Native Cedar bridge failed: {JsonWire.String(doc.RootElement, "message")}", status);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new CedarBridgeException("Native bridge returned an invalid response.", innerException: ex); }
        finally { if (output.Data != null) free(output); }
    }

    private static (string Rid, string File, string Target) Platform()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            return ("win-x64", "cedarsharp_native.dll", "x86_64-pc-windows-msvc");
        if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            !RuntimeInformation.RuntimeIdentifier.StartsWith("linux-musl", StringComparison.Ordinal))
            return ("linux-x64", "libcedarsharp_native.so", "x86_64-unknown-linux-gnu");
        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return ("osx-arm64", "libcedarsharp_native.dylib", "aarch64-apple-darwin");
        throw new PlatformNotSupportedException($"CedarSharp has no native runtime for {RuntimeInformation.RuntimeIdentifier}/{RuntimeInformation.ProcessArchitecture}.");
    }
}
