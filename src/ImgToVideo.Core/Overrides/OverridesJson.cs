using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Serialization;

namespace ImgToVideo.Core.Overrides;

public static class OverridesJson
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static ProjectOverrides LoadOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return new ProjectOverrides();
        }

        return Load(path);
    }

    public static ProjectOverrides Load(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static ProjectOverrides LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("schema_version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("overrides.json is missing schema_version.");
        }

        var version = versionElement.GetInt32();
        if (version != ProjectOverrides.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported overrides schema_version {version} (supported: {ProjectOverrides.CurrentSchemaVersion}).");
        }

        var overrides = JsonSerializer.Deserialize<ProjectOverrides>(json, JsonOptions);
        return overrides ?? throw new InvalidDataException("overrides.json is empty.");
    }

    public static void Save(ProjectOverrides overrides, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(overrides, JsonOptions));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new MotionTypeJsonConverter());
        options.Converters.Add(new EasingModeJsonConverter());
        options.Converters.Add(new TransitionKindJsonConverter());
        return options;
    }
}
