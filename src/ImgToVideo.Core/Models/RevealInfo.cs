namespace ImgToVideo.Core.Models;

/// <summary>How a reveal shot lays out its items.</summary>
public static class RevealLayouts
{
    /// <summary>Equal-width slices, left to right.</summary>
    public const string Row = "row";

    /// <summary>2x2 quadrants in reading order: top-left, top-right, bottom-left, bottom-right.</summary>
    public const string Grid = "grid";

    public static bool IsValid(string? layout) =>
        string.Equals(layout, Row, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(layout, Grid, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? layout) =>
        string.Equals(layout, Grid, StringComparison.OrdinalIgnoreCase) ? Grid : Row;
}

/// <summary>
/// The recipe of one reveal step. With <see cref="Sources"/> empty, the still shows the
/// first <see cref="Visible"/> of <see cref="Count"/> items of the clip's single source
/// image (the rest black). With Sources set, each item is its own image, placed
/// in its slot and cover-cropped; step k shows images 1..k.
/// </summary>
public sealed class RevealInfo
{
    public string Layout { get; set; } = RevealLayouts.Row;
    public int Count { get; set; }
    public int Visible { get; set; }

    /// <summary>The shot id the steps were expanded from.</summary>
    public string? BaseShotId { get; set; }

    /// <summary>The single source image of a slice/grid reveal (the whole picture).</summary>
    public string? SourcePath { get; set; }

    /// <summary>One image per item for a build-up of separate images; empty for a slice/grid reveal.</summary>
    public List<string> Sources { get; set; } = new();

    public bool IsStack => Sources.Count > 0;
}
