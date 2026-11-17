using System.Text.Json;
using System.Text.Json.Serialization;

namespace MulticlassRace.Services.QuickRace;

/// <summary>The one way quick race documents are written to and read from the database.</summary>
internal static class QuickRaceJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Null (never an exception) for a blank or unreadable document, so a damaged row cannot break the page.</summary>
    public static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
