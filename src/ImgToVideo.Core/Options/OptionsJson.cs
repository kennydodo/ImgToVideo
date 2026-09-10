using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Serialization;

namespace ImgToVideo.Core.Options;

public static class OptionsJson
{
    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static ProjectOptions LoadOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return new ProjectOptions();
        }

        return Load(path);
    }

    public static ProjectOptions Load(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static ProjectOptions LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("schema_version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("imgtovideo.json is missing schema_version.");
        }

        var version = versionElement.GetInt32();
        if (version != ProjectOptions.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported options schema_version {version} (supported: {ProjectOptions.CurrentSchemaVersion}).");
        }

        var options = JsonSerializer.Deserialize<ProjectOptions>(json, JsonOptions);
        return options ?? throw new InvalidDataException("imgtovideo.json is empty.");
    }

    public static void Save(ProjectOptions options, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(options, JsonOptions));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new TransitionKindJsonConverter());
        return options;
    }
}
