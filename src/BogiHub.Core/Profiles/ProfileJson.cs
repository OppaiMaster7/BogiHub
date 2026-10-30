using System.Text.Json;
using System.Text.Json.Serialization;

namespace BogiHub.Core.Profiles;

public static class ProfileJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static string Serialize(Profile profile) => JsonSerializer.Serialize(profile, Options);

    /// <summary>Parses JSON without validating values. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static Profile Deserialize(string json) =>
        JsonSerializer.Deserialize<Profile>(json, Options) ?? throw new JsonException("Profile file is empty.");
}
