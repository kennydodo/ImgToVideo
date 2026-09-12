namespace ImgToVideo.Core.Models;

public sealed class VideoClip
{
    public string FilePath { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;

    /// <summary>Identity of a manifest-planned shot; null for v1-planned clips.</summary>
    public string? ShotId { get; set; }
    public long StartFrame { get; set; }

    /// <summary>Frames after which the motion completes and the framing holds; null = move across the whole clip.</summary>
    public long? MotionDurationFrames { get; set; }
    public long DurationFrames { get; set; }
    public MotionType Motion { get; set; } = MotionType.Static;
    public MotionSource MotionSource { get; set; } = MotionSource.AutoSelected;
    public ImageType? ImageType { get; set; }
    public EasingMode Easing { get; set; } = EasingMode.Linear;
    public Rect StartViewport { get; set; }
    public Rect EndViewport { get; set; }
    public TransitionIn? Transition { get; set; }
}
