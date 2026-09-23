using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImgToVideo.Core.Manifest;

public sealed record MissingShotListResult(string Json, string PlainText, int Count);

/// <summary>
/// Builds the missing-image delta in two paste-ready shapes: a
/// shotlist.json-shaped payload (style + refs + images) and a plain-text
/// listing (master prompt, then one "file" / "prompt" line per image).
/// Both contain only the images[] entries whose file is not present in
/// images\ yet.
/// </summary>
public static class MissingShotList
{
    public const string ImagesDirectoryName = "images";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // prompts are hand-read and hand-pasted: keep em-dashes etc. literal
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Returns null when the project has no shotlist.json.</summary>
    public static MissingShotListResult? BuildForProject(string projectFolder)
    {
        var shotListPath = Path.Combine(projectFolder, ShotListParser.FileName);
        if (!File.Exists(shotListPath))
        {
            return null;
        }

        var imagesDir = Path.Combine(projectFolder, ImagesDirectoryName);
        var present = Directory.Exists(imagesDir)
            ? Directory.GetFiles(imagesDir).Select(Path.GetFileName).OfType<string>().ToList()
            : [];

        return BuildFromJson(File.ReadAllText(shotListPath), present);
    }

    public static MissingShotListResult BuildFromJson(string json, IReadOnlyCollection<string> presentFiles)
    {
        ArgumentNullException.ThrowIfNull(presentFiles);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("shotlist.json must be an object.");
        }

        var onDisk = new HashSet<string>(presentFiles, StringComparer.OrdinalIgnoreCase);
        var missing = new List<JsonNode?>();

        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                var file = image.ValueKind == JsonValueKind.Object &&
                    image.TryGetProperty("file", out var fileElement) &&
                    fileElement.ValueKind == JsonValueKind.String
                        ? fileElement.GetString()?.Trim()
                        : null;

                if (string.IsNullOrEmpty(file) || onDisk.Contains(file))
                {
                    continue;
                }

                missing.Add(JsonNode.Parse(image.GetRawText()));
            }
        }

        var style = root.TryGetProperty("style", out var styleElement) &&
            styleElement.ValueKind == JsonValueKind.String
                ? styleElement.GetString()?.Trim()
                : null;

        var result = new JsonObject();
        if (!string.IsNullOrEmpty(style))
        {
            result["style"] = style;
        }

        if (root.TryGetProperty("refs", out var refs) && refs.ValueKind == JsonValueKind.Object)
        {
            result["refs"] = JsonNode.Parse(refs.GetRawText());
        }

        var imagesArray = new JsonArray();
        foreach (var node in missing)
        {
            imagesArray.Add(node);
        }

        result["images"] = imagesArray;

        return new MissingShotListResult(
            result.ToJsonString(JsonOptions),
            BuildPlainText(style, missing),
            missing.Count);
    }

    private static string BuildPlainText(string? style, IReadOnlyList<JsonNode?> missing)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(style))
        {
            builder.Append("Master prompt: ").Append(style).AppendLine();
            builder.AppendLine();
        }

        foreach (var node in missing)
        {
            var file = node?["file"]?.GetValue<string>() ?? string.Empty;
            var prompt = node?["prompt"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : string.Empty;
            builder.Append("\"file\": \"").Append(file)
                .Append("\", \"prompt\": \"").Append(prompt).Append('"')
                .AppendLine();
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }
}
