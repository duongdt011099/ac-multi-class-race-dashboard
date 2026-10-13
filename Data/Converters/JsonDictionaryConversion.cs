using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MulticlassRace.Data.Converters;

/// <summary>Stores a string-to-int dictionary as one JSON text column.</summary>
public sealed class JsonDictionaryConverter : ValueConverter<Dictionary<string, int>, string>
{
    public JsonDictionaryConverter() : base(
        v => JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = false }),
        v => string.IsNullOrWhiteSpace(v)
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, int>())
    {
    }
}

/// <summary>Tells EF when a dictionary changed, since it is compared by content rather than by reference.</summary>
public sealed class JsonDictionaryComparer : ValueComparer<Dictionary<string, int>>
{
    public JsonDictionaryComparer() : base(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
        v => v.ToDictionary(kv => kv.Key, kv => kv.Value))
    {
    }
}
