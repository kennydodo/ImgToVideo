using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImgToVideo.Core.Manifest;

public sealed record VisualManifest(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    VisualManifestVideo Video,
    IReadOnlyList<VisualAsset> Assets,
    IReadOnlyList<VisualShot> Timeline);

public sealed record VisualManifestVideo(
    string? Id,
    string? Title,
    long? DurationMs,
    double? Fps);

public sealed record VisualAsset(
    string Id,
    string File,
    string? SceneId,
    IReadOnlyList<string>? BeatIds,
    string? Type,
    IReadOnlyList<string>? SafeMotion,
    bool? AllowReuse,
    IReadOnlyList<FocalRegion>? FocalRegions);

public sealed record FocalRegion(
    string Id,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record VisualShot(
    string ShotId,
    long StartMs,
    long EndMs,
    string? SceneId,
    string? BeatId,
    IReadOnlyList<int>? SrtCueIds,
    string? NarrationText,
    string? VisualIntent,
    string? ChangeReason,
    string AssetId,
    VisualFraming? Framing,
    VisualMotion? Motion,
    VisualTransition? TransitionOut);

public sealed record VisualFraming(string Type, string? FocalRegion);

public sealed record VisualMotion(
    string Type,
    double? StartScale,
    double? EndScale,
    long? DurationMs);

public sealed record VisualTransition(string Type, long DurationMs);

public static class VisualManifestLoader
{
    public const string SupportedSchemaVersion = "1.0";
    public const string FileName = "visual_manifest.json";

    public static bool Exists(string projectFolder) =>
        File.Exists(Path.Combine(projectFolder, FileName));

    /// <summary>Returns null when no manifest exists; throws InvalidDataException/JsonException on a broken one.</summary>
    public static VisualManifest? Load(string projectFolder)
    {
        var path = Path.Combine(projectFolder, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("schema_version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("visual_manifest.json is missing schema_version.");
        }

        var version = versionElement.GetString();
        if (version != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported visual_manifest schema_version \"{version}\" (supported: \"{SupportedSchemaVersion}\").");
        }

        return JsonSerializer.Deserialize<VisualManifest>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Writes a manifest back to disk in the canonical snake_case form.</summary>
    public static void Save(VisualManifest manifest, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, SaveOptions));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return options;
    }

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
}
