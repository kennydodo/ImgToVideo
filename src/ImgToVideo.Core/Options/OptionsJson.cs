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
        if (string.IsNullOrWhiteSpace(json))
        {
            // Empty file (e.g. created but not saved yet) — use defaults.
            return new ProjectOptions();
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("imgtovideo.json must contain a JSON object.");
        }

        if (!doc.RootElement.TryGetProperty("schema_version", out var versionElement))
        {
            // Hand-written file without a version stamp: apply whatever fields
            // it sets on top of the defaults.
            return JsonSerializer.Deserialize<ProjectOptions>(json, JsonOptions) ?? new ProjectOptions();
        }

        if (versionElement.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidDataException("imgtovideo.json has a non-numeric schema_version.");
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
        options.Converters.Add(new TransitionAlignmentJsonConverter());
        options.Converters.Add(new EasingModeJsonConverter());
        return options;
    }
}
