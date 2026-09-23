using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Reporting;

public sealed record DiagnosticsGroup(string Title, IReadOnlyList<ValidationIssue> Issues)
{
    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);

    public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);

    public bool HasErrors => ErrorCount > 0;

    public string Summary()
    {
        var parts = new List<string>(3);
        if (ErrorCount > 0)
        {
            parts.Add(Plural(ErrorCount, "error"));
        }

        if (WarningCount > 0)
        {
            parts.Add(Plural(WarningCount, "warning"));
        }

        if (InfoCount > 0)
        {
            parts.Add(Plural(InfoCount, "info"));
        }

        return string.Join(", ", parts);
    }

    private static string Plural(int count, string noun) =>
        $"{count} {noun}{(count == 1 ? string.Empty : "s")}";
}

public static class DiagnosticsGrouper
{
    public const string MissingImagesTitle = "Missing images";

    private const string ImagesTitle = "Images";
    private const string FallbackTitle = "Other";

    /// <summary>
    /// Explicit code → group mapping. Beats the prefix table so a code can be
    /// pulled out of its namespace (e.g. SHOTLIST_GENERATE_AHEAD is really a
    /// missing image, not a shot-list problem).
    /// </summary>
    private static readonly Dictionary<string, (string Title, int Order)> Codes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["IMAGES_MISSING"] = (MissingImagesTitle, 0),
            ["IMAGES_EMPTY"] = (MissingImagesTitle, 0),
            ["IMAGES_NONE"] = (MissingImagesTitle, 0),
            ["IMAGE_NO_DIMENSIONS"] = (MissingImagesTitle, 0),
            ["SHOTLIST_GENERATE_AHEAD"] = (MissingImagesTitle, 0),
        };

    private static readonly (string Prefix, string Title, int Order)[] Prefixes =
    [
        ("IMAGE", ImagesTitle, 1),
        ("IMAGES", ImagesTitle, 1),
        ("PNG", ImagesTitle, 1),
        ("SHOTLIST", "Shot list", 2),
        ("SHOT", "Shot list", 2),
        ("AUDIO", "Audio", 3),
        ("SRT", "Narration / SRT", 4),
        ("MANIFEST", "Manifest", 5),
        ("SCENE", "Scenes & planning", 6),
        ("PLANNER", "Scenes & planning", 6),
        ("TIMING", "Timing", 7),
        ("MOTION", "Motion", 8),
        ("VIEWPORT", "Motion", 8),
        ("COVERAGE", "Coverage", 9),
        ("OVERRIDES", "Overrides", 10),
    ];

    public static IReadOnlyList<DiagnosticsGroup> Group(IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return issues
            .Select((issue, index) => (Issue: issue, Index: index))
            .GroupBy(entry => Category(entry.Issue.Code))
            .Select(group => new
            {
                Category = group.Key,
                Issues = group
                    .OrderBy(entry => SeverityRank(entry.Issue.Severity))
                    .ThenBy(entry => entry.Index)
                    .Select(entry => entry.Issue)
                    .ToList(),
            })
            .OrderByDescending(group => group.Issues.Any(i => i.Severity == ValidationSeverity.Error))
            .ThenBy(group => group.Category.Order)
            .ThenBy(group => group.Category.Title, StringComparer.Ordinal)
            .Select(group => new DiagnosticsGroup(group.Category.Title, group.Issues))
            .ToList();
    }

    private static (string Title, int Order) Category(string code)
    {
        if (Codes.TryGetValue(code, out var exact))
        {
            return exact;
        }

        foreach (var (prefix, title, order) in Prefixes)
        {
            if (code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return (title, order);
            }
        }

        return (FallbackTitle, 11);
    }

    private static int SeverityRank(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => 0,
        ValidationSeverity.Warning => 1,
        _ => 2,
    };
}
