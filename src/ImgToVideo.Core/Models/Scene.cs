namespace ImgToVideo.Core.Models;

public sealed class Scene
{
    public string Id { get; set; } = string.Empty;
    public long StartFrame { get; set; }
    public long EndFrame { get; set; }
    public List<VideoClip> Clips { get; set; } = new();
}
