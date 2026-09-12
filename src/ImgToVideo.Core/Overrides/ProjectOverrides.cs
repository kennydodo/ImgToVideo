using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Overrides;

public sealed class ClipOverride
{
    public string File { get; set; } = string.Empty;

    /// <summary>Manifest shot id; set for v2-planned clips (takes precedence over File matching).</summary>
    public string? Shot { get; set; }
    public MotionType? Motion { get; set; }
    public EasingMode? Easing { get; set; }
    public long? DurationFrames { get; set; }
    public bool Exclude { get; set; }
    public int? Order { get; set; }
}

public sealed class CutOverride
{
    public string BeforeFile { get; set; } = string.Empty;
    public TransitionKind Transition { get; set; } = TransitionKind.Crossfade;
}

public sealed class ProjectOverrides
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<ClipOverride> Clips { get; set; } = new();
    public List<CutOverride> Cuts { get; set; } = new();

    public ClipOverride? ForClip(string filePath)
    {
        var key = Normalize(filePath);
        return Clips.FirstOrDefault(c => string.Equals(Normalize(c.File), key, StringComparison.OrdinalIgnoreCase));
    }

    public ClipOverride? ForShot(string shotId)
    {
        if (string.IsNullOrEmpty(shotId))
        {
            return null;
        }

        return Clips.FirstOrDefault(c =>
            string.Equals(c.Shot, shotId, StringComparison.OrdinalIgnoreCase));
    }

    public TransitionKind? CutFor(string incomingFilePath)
    {
        var key = Normalize(incomingFilePath);
        return Cuts.FirstOrDefault(c => string.Equals(Normalize(c.BeforeFile), key, StringComparison.OrdinalIgnoreCase))?.Transition;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/');
}
