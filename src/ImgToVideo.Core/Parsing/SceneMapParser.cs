using System.Text.Json;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Parsing;

public sealed record SceneMapParseResult(IReadOnlyList<SceneWindow> Windows, IReadOnlyList<ValidationIssue> Issues);

public static class SceneMapParser
{
    public static SceneMapParseResult ParseFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (IOException e)
        {
            return Fail($"scenes.json could not be read: {e.Message}");
        }
    }

    public static SceneMapParseResult Parse(string json)
    {
        var issues = new List<ValidationIssue>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            return Fail($"scenes.json is not valid JSON: {e.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Fail("scenes.json must contain a JSON array of scene entries.");
            }

            var windows = new List<SceneWindow>();
            var seenScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("scene", out var sceneElement) ||
                    sceneElement.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("start", out var startElement) ||
                    !TryGetNumber(startElement, out var start) ||
                    !element.TryGetProperty("end", out var endElement) ||
                    !TryGetNumber(endElement, out var end))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SCENE_MAP_ENTRY",
                        "scenes.json contains an invalid entry (expected { scene, start, end })."));
                    continue;
                }

                var sceneId = sceneElement.GetString() ?? string.Empty;
                if (sceneId.Length == 0)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SCENE_MAP_ENTRY", "scenes.json contains an entry with an empty scene id."));
                    continue;
                }

                if (!seenScenes.Add(sceneId))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SCENE_MAP_DUPLICATE",
                        $"scenes.json lists scene {sceneId} more than once."));
                    continue;
                }

                if (end <= start)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SCENE_MAP_RANGE",
                        $"scenes.json entry {sceneId} has end <= start ({end} <= {start})."));
                    continue;
                }

                windows.Add(new SceneWindow(sceneId, start, end));
            }

            windows.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));

            for (var i = 1; i < windows.Count; i++)
            {
                if (windows[i].StartSeconds < windows[i - 1].EndSeconds - 0.001)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SCENE_MAP_OVERLAP",
                        $"scenes.json scenes {windows[i - 1].SceneId} and {windows[i].SceneId} overlap."));
                }
            }

            return new SceneMapParseResult(windows, issues);
        }
    }

    private static bool TryGetNumber(JsonElement element, out double value)
    {
        if (element.ValueKind is JsonValueKind.Number)
        {
            return element.TryGetDouble(out value);
        }

        value = 0;
        return false;
    }

    private static SceneMapParseResult Fail(string message) =>
        new([], [new ValidationIssue(ValidationSeverity.Error, "SCENE_MAP", message)]);
}
