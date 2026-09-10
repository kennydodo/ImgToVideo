namespace ImgToVideo.Core.Models;

public sealed class VideoClip
{
    public string FilePath { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;
    public long StartFrame { get; set; }
    public long DurationFrames { get; set; }
    public MotionType Motion { get; set; } = MotionType.Static;
    public MotionSource MotionSource { get; set; } = MotionSource.AutoSelected;
    public ImageType? ImageType { get; set; }
    public Rect StartViewport { get; set; }
    public Rect EndViewport { get; set; }
    public TransitionIn? Transition { get; set; }
}
