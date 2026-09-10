using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Serialization;

public static class TimelineJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static void Save(Timeline timeline, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(timeline, Options));
    }

    public static Timeline Load(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static Timeline LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("schema_version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("timeline.json is missing schema_version.");
        }

        var version = versionElement.GetInt32();
        if (version != Timeline.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported timeline schemaVersion {version} (supported: {Timeline.CurrentSchemaVersion}).");
        }

        var timeline = JsonSerializer.Deserialize<Timeline>(json, Options);
        return timeline ?? throw new InvalidDataException("timeline.json is empty.");
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
        options.Converters.Add(new MotionSourceJsonConverter());
        options.Converters.Add(new TransitionKindJsonConverter());
        options.Converters.Add(new ImageTypeJsonConverter());
        return options;
    }
}
