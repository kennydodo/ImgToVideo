namespace ImgToVideo.Core.Models;

/// <summary>
/// The built-in "pop" sounds. A shotlist says <c>"sfx": "pop"</c>; which of these actually plays
/// is the project's <c>Sound.DefaultPop</c> setting. Each id is also usable by name in a shotlist
/// (e.g. <c>"sfx": "pop_marimba"</c>). The audio recipes live in ImgToVideo.Ffmpeg.SoundEffectLibrary.
/// </summary>
public static class SoundCatalog
{
    public const string PopAlias = "pop";
    public const string DefaultPopId = "pop_bubble";

    public static readonly IReadOnlyList<(string Id, string Label)> PopVariants =
    [
        ("pop_bubble", "Bubble - short rising pop"),
        ("pop_boop", "Boop - soft, rounded"),
        ("pop_drop", "Drop - falling water drop"),
        ("pop_blip", "Blip - tiny, clean"),
        ("pop_tap", "Tap - dry wood tap"),
        ("pop_snap", "Snap - finger snap"),
        ("pop_cork", "Cork - bottle-cork pop"),
        ("pop_thump", "Thump - low, soft bass"),
        ("pop_pluck", "Pluck - plucked string"),
        ("pop_marimba", "Marimba - mallet note"),
        ("pop_ping", "Ping - glass ping"),
        ("pop_sparkle", "Sparkle - two quick rising notes"),
        ("pop_classic", "Classic - the original pop"),
    ];

    public static bool IsPopVariant(string? id) =>
        id is not null && PopVariants.Any(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
}
