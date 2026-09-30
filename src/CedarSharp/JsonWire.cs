using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace CedarSharp;

internal static class JsonWire
{
    internal const int MaxInputBytes = 16 * 1024 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 256 };
    internal static readonly JsonSerializerOptions SerializerOptions = new() { MaxDepth = 256 };
    internal static JsonElement Parse(string json, JsonValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (StrictUtf8.GetByteCount(json) > MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.", nameof(json));
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        if (doc.RootElement.ValueKind != kind) throw new ArgumentException($"Expected a JSON {kind}.", nameof(json));
        return doc.RootElement.Clone();
    }
    internal static JsonElement FromNode(JsonNode? node)
    {
        var json = node?.ToJsonString() ?? "null";
        if (StrictUtf8.GetByteCount(json) > MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.", nameof(node));
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        return doc.RootElement.Clone();
    }
    internal static JsonElement FromObject(JsonElement element)
    {
        EnsureSize(element);
        return element.Clone();
    }
    internal static JsonElement StringElement(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Build(w => w.WriteStringValue(value));
    }
    internal static JsonElement FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var element = JsonSerializer.SerializeToElement(value, typeInfo);
        EnsureSize(element);
        return element.Clone();
    }
    [RequiresUnreferencedCode("Serializing arbitrary CLR objects uses reflection. Use JsonElement, JsonNode, or a JsonTypeInfo overload for trim/AOT.")]
    [RequiresDynamicCode("Serializing arbitrary CLR objects uses reflection. Use JsonElement, JsonNode, or a JsonTypeInfo overload for trim/AOT.")]
    internal static JsonElement FromObjectReflection<T>(T? value)
    {
        if (value is JsonElement e) return FromObject(e);
        if (value is JsonNode n) return FromNode(n);
        if (value is string s) return Parse(s, JsonValueKind.Object);
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        EnsureSize(element);
        return element.Clone();
    }
    private static void EnsureSize(JsonElement element)
    {
        if (StrictUtf8.GetByteCount(element.GetRawText()) > MaxInputBytes) throw new ArgumentException("Cedar input exceeds 16 MiB.");
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
    internal static string? OptionalString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() : null;
    internal static long RequiredInt64(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) throw new JsonException($"Missing {name}.");
        return v.GetInt64();
    }
    /// <summary>Reads a required array. A missing, null, or non-array value is a response-contract violation.</summary>
    internal static IReadOnlyList<T> Array<T>(JsonElement e, string name, Func<JsonElement, T> read) =>
        System.Array.AsReadOnly(RequireArray(e, name).EnumerateArray().Select(read).ToArray());
    /// <summary>Reads an optional array. Absent or JSON null yields an empty list; a present non-array value is a contract violation.</summary>
    internal static IReadOnlyList<T> OptionalArray<T>(JsonElement e, string name, Func<JsonElement, T> read) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? System.Array.AsReadOnly(RequireArray(e, name).EnumerateArray().Select(read).ToArray())
            : System.Array.AsReadOnly(System.Array.Empty<T>());
    private static JsonElement RequireArray(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) throw new JsonException($"Missing required array '{name}'.");
        if (v.ValueKind != JsonValueKind.Array) throw new JsonException($"Field '{name}' must be a JSON array, found {v.ValueKind}.");
        return v;
    }
    internal static bool Success(JsonElement e) => String(e, "type") switch
    {
        "success" => true,
        "failure" => false,
        _ => throw new JsonException("Unknown Cedar answer type.")
    };
}