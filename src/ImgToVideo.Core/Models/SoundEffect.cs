namespace ImgToVideo.Core.Models;

/// <summary>A short sound laid over the narration at a frame.</summary>
public sealed class SoundEffect
{
    /// <summary>The name the shotlist used (pop, whoosh, a file stem in the project's sfx folder, ...).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Resolved audio file; empty until the sound has been looked up.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long StartFrame { get; set; }

    /// <summary>Length of the sound in frames; 0 until resolved.</summary>
    public long DurationFrames { get; set; }
}
