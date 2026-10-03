using System.Collections;
using System.Text.Json;
using IdempotencyEngine.Domain.Interfaces;

namespace IdempotencyEngine.Domain.Services;

/// <summary>
/// A service that generates a deterministic fingerprint for a given context represented as a dictionary of key-value pairs.
/// </summary>
public class FingerprintGenerator : IFingerprintGenerator
{
    private readonly IHasher _hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="FingerprintGenerator"/> class.
    /// </summary>
    /// <param name="hasher">The hasher instance to use. If null, a default <see cref="Sha256Hasher"/> instance is used.</param>
    public FingerprintGenerator(IHasher? hasher = null)
    {
        _hasher = hasher ?? new Sha256Hasher();
    }

    /// <summary>
    /// Computes a deterministic fingerprint for the given context. 
    /// Output - fingerprint hash
    /// </summary>
    /// <param name="context">The context dictionary containing key-value pairs to fingerprint.</param>
    /// <returns>A lowercase hexadecimal SHA256 fingerprint hash of the context dictionary.</returns>
    public string ComputeContextFingerprint(IDictionary<string, object>? context)
    {
        if (context == null || context.Count == 0)
        {
            return _hasher.ComputeSha256("{}");
        }

        var sortedContext = SortKeysRecursively(context);
        var jsonString = JsonSerializer.Serialize(sortedContext);
        return _hasher.ComputeSha256(jsonString);
    }

    /// <summary>
    /// Recursively sorts the keys of a dictionary and any nested dictionaries or lists to ensure consistent ordering for fingerprint generation.
    /// </summary>
    /// <param name="value">The object, dictionary, or list to recursively sort.</param>
    /// <returns>A sorted object representation for JSON serialization.</returns>
    private static object? SortKeysRecursively(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => SortKeysFromJsonObject(element),
                JsonValueKind.Array => SortKeysFromJsonArray(element),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.TryGetDouble(out double d) ? d : element.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => element.GetRawText()
            };
        }

        if (value is IDictionary dict)
        {
            var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString() ?? string.Empty;
                sorted[key] = SortKeysRecursively(entry.Value);
            }
            return sorted;
        }

        if (value is IEnumerable enumerable && !(value is string))
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(SortKeysRecursively(item));
            }
            return list;
        }

        return value;
    }

    /// <summary>
    /// Recursively sorts the keys of a JsonElement representing a JSON object to ensure consistent ordering for fingerprint generation.
    /// </summary>
    /// <param name="element">The JsonElement representing a JSON object.</param>
    /// <returns>A sorted dictionary with the same keys and values.</returns>
    private static SortedDictionary<string, object?> SortKeysFromJsonObject(JsonElement element)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            sorted[prop.Name] = SortKeysRecursively(prop.Value);
        }
        return sorted;
    }

    /// <summary>
    /// Recursively sorts the elements of a JsonElement representing a JSON array to ensure consistent ordering for fingerprint generation.
    /// </summary>
    /// <param name="element">The JsonElement representing a JSON array.</param>
    /// <returns> A sorted list of objects.</returns>
    private static List<object?> SortKeysFromJsonArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(SortKeysRecursively(item));
        }
        return list;
    }
}
