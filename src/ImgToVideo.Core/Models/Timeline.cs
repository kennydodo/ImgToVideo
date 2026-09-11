namespace ImgToVideo.Core.Models;

public sealed class Timeline
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ProjectName { get; set; } = string.Empty;
    public Resolution Resolution { get; set; } = new(1920, 1080);
    public double Fps { get; set; } = 30.0;
    public AudioTrack Audio { get; set; } = new();
    public List<Scene> Scenes { get; set; } = new();
}
