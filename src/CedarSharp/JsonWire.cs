using System.Text;
using System.Text.Json;

namespace CedarSharp;

internal static class JsonWire
{
    internal const int MaxInputBytes = 16 * 1024 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static JsonElement Parse(string json, JsonValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (StrictUtf8.GetByteCount(json) > MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.", nameof(json));
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 256 });
        if (doc.RootElement.ValueKind != kind) throw new ArgumentException($"Expected a JSON {kind}.", nameof(json));
        return doc.RootElement.Clone();
    }
    internal static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        if (stream.Length > MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.");
        return stream.ToArray();
    }
    internal static JsonElement Build(Action<Utf8JsonWriter> write)
    {
        using var doc = JsonDocument.Parse(Encode(write), new JsonDocumentOptions { MaxDepth = 256 });
        return doc.RootElement.Clone();
    }
    internal static string String(JsonElement e, string name) => e.GetProperty(name).GetString() ?? throw new JsonException($"Missing {name}.");
    internal static IReadOnlyList<T> Array<T>(JsonElement e, string name, Func<JsonElement, T> read) =>
        System.Array.AsReadOnly(e.GetProperty(name).EnumerateArray().Select(read).ToArray());
    internal static bool Success(JsonElement e) => String(e, "type") switch
    {
        "success" => true,
        "failure" => false,
        _ => throw new JsonException("Unknown Cedar answer type.")
    };
}