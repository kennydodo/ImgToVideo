using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Manifest;

/// <summary>
/// One shot from shotlist.json: the LLM decides which cues an image covers and
/// how it moves. Filenames follow the generation spec; timing is derived here.
/// </summary>
public sealed record ShotListEntry(
    string? ShotId,
    int FirstCue,
    int LastCue,
    string Asset,
    VisualMotion? Motion,
    VisualTransition? Transition,
    VisualFraming? Framing,
    string? Scene);

public sealed record ShotListDocument(
    IReadOnlyList<ShotListEntry> Shots,
    IReadOnlyDictionary<string, string> Prompts,
    string? Style = null);

public static class ShotListParser
{
    public const string FileName = "shotlist.json";

    /// <summary>Parses the LLM format: a shots array plus an optional images
    /// array (file → prompt) that doubles as the batch image app's input.
    /// Throws JsonException/InvalidDataException on a broken file.</summary>
    public static ShotListDocument Parse(string json, List<ValidationIssue> issues)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("shots", out var shotsElement) ||
            shotsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "shotlist.json must be an object with a \"shots\" array.");
        }

        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? style = null;
        if (root.TryGetProperty("style", out var styleElement) &&
            styleElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(styleElement.GetString()))
        {
            style = styleElement.GetString()!.Trim();
        }

        if (root.TryGetProperty("images", out var imagesElement) &&
            imagesElement.ValueKind == JsonValueKind.Array)
        {
            var imageNumber = 0;
            foreach (var image in imagesElement.EnumerateArray())
            {
                imageNumber++;
                if (image.ValueKind != JsonValueKind.Object ||
                    !image.TryGetProperty("file", out var fileElement) ||
                    fileElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(fileElement.GetString()))
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SHOTLIST_INVALID",
                        $"shotlist.json: images[{imageNumber}] needs a \"file\" name."));
                    continue;
                }

                var prompt = string.Empty;
                if (image.TryGetProperty("prompt", out var promptElement) &&
                    promptElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(promptElement.GetString()))
                {
                    prompt = promptElement.GetString()!.Trim();
                }

                prompts.TryAdd(fileElement.GetString()!.Trim(), prompt);
            }
        }

        var entries = new List<ShotListEntry>();
        var number = 0;
        foreach (var shot in shotsElement.EnumerateArray())
        {
            number++;
            if (shot.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SHOTLIST_INVALID",
                    $"shotlist.json: shots[{number}] is not an object."));
                continue;
            }

            if (!TryGetCues(shot, out var firstCue, out var lastCue))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SHOTLIST_CUES_INVALID",
                    $"shotlist.json: shots[{number}] has no usable \"cues\" — " +
                    "use \"1-3\", \"4\" or [1,2,3]."));
                continue;
            }

            if (!shot.TryGetProperty("asset", out var assetElement) ||
                assetElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(assetElement.GetString()))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SHOTLIST_INVALID",
                    $"shotlist.json: shots[{number}] is missing \"asset\" (the image filename)."));
                continue;
            }

            entries.Add(new ShotListEntry(
                GetString(shot, "shot_id"),
                firstCue,
                lastCue,
                assetElement.GetString()!.Trim(),
                ParseMotion(TryGet(shot, "motion")),
                ParseTransition(TryGet(shot, "transition")),
                ParseFraming(TryGet(shot, "framing")),
                GetString(shot, "scene")));
        }

        return new ShotListDocument(entries, prompts, style);
    }

    private static bool TryGetCues(JsonElement shot, out int firstCue, out int lastCue)
    {
        firstCue = lastCue = 0;
        if (!shot.TryGetProperty("cues", out var cues))
        {
            return false;
        }

        var numbers = new List<int>();
        if (cues.ValueKind == JsonValueKind.String)
        {
            foreach (var part in cues.GetString()!.Split(','))
            {
                var range = part.Trim().Split('-', '/');
                if (range.Length is not (1 or 2))
                {
                    return false;
                }

                if (!int.TryParse(range[0].Trim(), out var a) || a < 1)
                {
                    return false;
                }

                numbers.Add(a);
                if (range.Length == 2)
                {
                    if (!int.TryParse(range[1].Trim(), out var b) || b < 1)
                    {
                        return false;
                    }

                    numbers.Add(b);
                }
            }
        }
        else if (cues.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in cues.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !TryGetInt(item, out var value))
                {
                    return false;
                }

                numbers.Add(value);
            }
        }
        else
        {
            return false;
        }

        if (numbers.Count == 0 || numbers.Any(n => n < 1))
        {
            return false;
        }

        firstCue = numbers.Min();
        lastCue = numbers.Max();
        return true;
    }

    private static JsonElement? TryGet(JsonElement shot, string name) =>
        shot.TryGetProperty(name, out var element) ? element : null;

    private static string? GetString(JsonElement shot, string name) =>
        TryGet(shot, name) is { } element && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryGetInt(JsonElement element, out int value)
    {
        try
        {
            value = element.GetInt32();
            return true;
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
    }

    private static VisualMotion? ParseMotion(JsonElement? element)
    {
        if (element is not { } e)
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.String => new VisualMotion(e.GetString()!, null, null, null),
            JsonValueKind.Object => new VisualMotion(
                GetString(e, "type") ?? "STATIC",
                GetNumber(e, "start_scale"),
                GetNumber(e, "end_scale"),
                GetLong(e, "duration_ms")),
            _ => null,
        };
    }

    private static VisualTransition? ParseTransition(JsonElement? element)
    {
        if (element is not { } e)
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.String => new VisualTransition(e.GetString()!, 0),
            JsonValueKind.Object => new VisualTransition(
                GetString(e, "type") ?? "CUT",
                GetLong(e, "duration_ms") ?? 0),
            _ => null,
        };
    }

    private static VisualFraming? ParseFraming(JsonElement? element)
    {
        if (element is not { } e)
        {
            return null;
        }

        return e.ValueKind switch
        {
            JsonValueKind.String => new VisualFraming(e.GetString()!, null),
            JsonValueKind.Object => new VisualFraming(
                GetString(e, "type") ?? "wide",
                GetString(e, "focal_region")),
            _ => null,
        };
    }

    private static double? GetNumber(JsonElement element, string name) =>
        TryGet(element, name) is { ValueKind: JsonValueKind.Number } e ? e.GetDouble() : null;

    private static long? GetLong(JsonElement element, string name) =>
        TryGet(element, name) is { ValueKind: JsonValueKind.Number } e ? e.GetInt64() : null;
}

/// <summary>Expands the shot list into a full VisualManifest (schema 1.0).
/// Filenames that are not on disk yet are kept as GENERATE requests; the
/// planner turns them into hints with narration and prompt. Likely typos of
/// existing filenames are rejected with a suggestion instead.</summary>
public static class ShotListExpander
{
    private const int TypoDistanceThreshold = 2;

    public static VisualManifest? Expand(
        ShotListDocument document,
        IReadOnlyList<SubtitleBlock> subtitles,
        IReadOnlyList<string> imageFileNames,
        TransitionOptions transitions,
        List<ValidationIssue> issues)
    {
        var entries = document.Shots;
        if (entries.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SHOTLIST_EMPTY",
                "shotlist.json contains no usable shots."));
            return null;
        }

        if (subtitles.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SHOTLIST_NO_SUBTITLES",
                "The SRT has no subtitle blocks; shotlist timings cannot be derived."));
            return null;
        }

        var byIndex = new Dictionary<int, SubtitleBlock>();
        for (var i = 0; i < subtitles.Count; i++)
        {
            var key = subtitles[i].Index > 0 ? subtitles[i].Index : i + 1;
            byIndex.TryAdd(key, subtitles[i]);
        }

        var byStem = imageFileNames
            .Select(f => (File: f, Stem: Path.GetFileNameWithoutExtension(f)))
            .GroupBy(x => x.Stem, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().File, StringComparer.OrdinalIgnoreCase);

        // 1. Resolve cues, assets and times per entry.
        var planned = new List<PlannedShot>();
        for (var n = 0; n < entries.Count; n++)
        {
            var entry = entries[n];
            if (!byIndex.TryGetValue(entry.FirstCue, out var firstBlock) ||
                !byIndex.TryGetValue(entry.LastCue, out var lastBlock))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SHOTLIST_CUE_UNKNOWN",
                    $"Shot {n + 1} (asset {entry.Asset}): cue {entry.FirstCue}–{entry.LastCue} does not exist — " +
                    $"the SRT has {subtitles.Count} blocks. Cue numbers are the SRT indices."));
                continue;
            }

            string file;
            string? intent = null;
            if (byStem.TryGetValue(Path.GetFileNameWithoutExtension(entry.Asset), out var resolved))
            {
                file = resolved;
            }
            else
            {
                var suggestion = imageFileNames
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .Select(stem => (Stem: stem, Distance: Levenshtein(
                        Normalize(entry.Asset), Normalize(stem))))
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (suggestion.Stem is not null && suggestion.Distance <= TypoDistanceThreshold)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Error, "SHOTLIST_ASSET_TYPO",
                        $"Shot {n + 1}: asset \"{entry.Asset}\" looks like a typo of " +
                        $"\"{suggestion.Stem}\" — fix the name (or if it really is a new image, use the " +
                        "generation-spec filename for it)."));
                    continue;
                }

                // Not on disk and not a typo: a GENERATE request. The filename
                // must follow the generation spec so the batch app can produce it.
                file = entry.Asset;
                if (!Path.HasExtension(file))
                {
                    file += ".png";
                }

                document.Prompts.TryGetValue(entry.Asset, out var prompt);
                intent = prompt;
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "SHOTLIST_GENERATE_AHEAD",
                    $"Shot {n + 1}: \"{file}\" is not in images\\ yet — it is planned as a GENERATE request; " +
                    "the build report lists exactly what to draw."));
            }

            var startMs = (long)Math.Round(firstBlock.StartSeconds * 1000);
            var endMs = (long)Math.Round(lastBlock.EndSeconds * 1000);
            if (endMs <= startMs)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "SHOTLIST_CUE_EMPTY",
                    $"Shot {n + 1} (asset {entry.Asset}): cues {entry.FirstCue}–{entry.LastCue} have zero duration; dropped."));
                continue;
            }

            var narration = Regex.Replace(
                string.Join(" ", Enumerable.Range(entry.FirstCue, entry.LastCue - entry.FirstCue + 1)
                    .Where(byIndex.ContainsKey)
                    .Select(c => byIndex[c].Text)),
                @"\s+", " ").Trim();

            planned.Add(new PlannedShot(
                entry.ShotId, n + 1, entry.FirstCue, entry.LastCue, file, file,
                startMs, endMs, narration,
                entry.Motion, entry.Transition, entry.Framing, entry.Scene,
                intent ?? document.Prompts.GetValueOrDefault(file)));
        }

        // 2. Narration decides the order: sort by start, note reorders.
        var sorted = planned.OrderBy(p => p.StartMs).ToList();
        var reordered = planned
            .Zip(sorted, (a, b) => !ReferenceEquals(a, b))
            .Any(different => different);
        if (reordered)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "SHOTLIST_REORDERED",
                "shotlist.json entries were not in narration order; they were sorted by their cue start times."));
        }

        // 3. Duplicate auto/explicit ids break overrides — keep the first.
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sorted.Count; i++)
        {
            var id = sorted[i].ShotId;
            if (id is null)
            {
                continue;
            }

            if (!seenIds.Add(id))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SHOTLIST_DUPLICATE_ID",
                    $"shotlist.json: shot id \"{id}\" is used more than once; the later shot was dropped."));
                sorted[i] = sorted[i] with { Dropped = true };
            }
        }

        // 4. Overlaps: narration never shows two images at once. Fully covered
        //    shots are dropped; partially overlapping shots are clamped to start
        //    after the previous one. Gaps left by dropped entries are closed by
        //    the planner's contiguity fix.
        var kept = new List<PlannedShot>();
        foreach (var original in sorted)
        {
            var shot = original;
            if (shot.Dropped)
            {
                continue;
            }

            if (kept.Count > 0 && shot.StartMs < kept[^1].EndMs)
            {
                if (shot.EndMs <= kept[^1].EndMs)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "SHOTLIST_CUES_OVERLAP",
                        $"Shot {shot.EntryNumber} (cues {shot.FirstCue}–{shot.LastCue}) is fully covered by the " +
                        "previous shot's cues; dropped."));
                    continue;
                }

                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "SHOTLIST_CUES_OVERLAP",
                    $"Shot {shot.EntryNumber} (cues {shot.FirstCue}–{shot.LastCue}) overlaps the previous shot; " +
                    "its start was clamped to the previous shot's end."));
                shot = shot with { StartMs = kept[^1].EndMs };
                if (shot.EndMs <= shot.StartMs)
                {
                    continue;
                }
            }

            kept.Add(shot);
        }

        if (kept.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SHOTLIST_EMPTY",
                "shotlist.json produced no usable shots (unknown assets or cue problems)."));
            return null;
        }

        // 5. Materialize the full manifest — the planner owns everything else.
        var assets = new List<VisualAsset>();
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shots = new List<VisualShot>();
        foreach (var shot in kept)
        {
            if (assetIds.Add(shot.AssetId))
            {
                assets.Add(new VisualAsset(shot.AssetId, shot.File, null, null, null, null, null, null));
            }

            shots.Add(new VisualShot(
                shot.ShotId ?? $"shot-{shot.EntryNumber:000}",
                shot.StartMs,
                shot.EndMs,
                shot.Scene ?? "S01",
                $"c{shot.FirstCue}",
                Enumerable.Range(shot.FirstCue, shot.LastCue - shot.FirstCue + 1).ToList(),
                shot.Narration,
                shot.Intent,
                null,
                shot.AssetId,
                shot.Framing,
                shot.Motion,
                shot.Transition is { } transition && !string.Equals(transition.Type, "CUT", StringComparison.OrdinalIgnoreCase)
                    ? transition.Type.ToUpperInvariant() is { } type && type is "CROSSFADE" or "DIP" or "DIP_WHITE"
                        ? new VisualTransition(type, (long)Math.Round(transitions.DurationSeconds * 1000))
                        : transition
                    : null));
        }

        return new VisualManifest(
            "1.0",
            new VisualManifestVideo("shotlist", null, null, null),
            assets,
            shots);
    }

    private static string Normalize(string name) =>
        Regex.Replace(Path.GetFileNameWithoutExtension(name).ToLowerInvariant(), @"[^a-z0-9]", "");

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private sealed record PlannedShot(
        string? ShotId,
        int EntryNumber,
        int FirstCue,
        int LastCue,
        string AssetId,
        string File,
        long StartMs,
        long EndMs,
        string Narration,
        VisualMotion? Motion,
        VisualTransition? Transition,
        VisualFraming? Framing,
        string? Scene,
        string? Intent,
        bool Dropped = false);
}
