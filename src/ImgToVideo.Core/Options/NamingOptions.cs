namespace ImgToVideo.Core.Options;

public sealed class NamingOptions
{
    public string ScenePrefix { get; set; } = "S";
    public int NumberPadding { get; set; } = 2;
    public string Separator { get; set; } = "_";
    public bool MotionCodesEnabled { get; set; } = true;
    public bool TypeCodesEnabled { get; set; } = true;
    public List<string> ImageExtensions { get; set; } = [".png"];

    public string SceneId(int sceneNumber)
    {
        var padding = Math.Clamp(NumberPadding, 1, 4);
        return $"{ScenePrefix}{sceneNumber.ToString(new string('0', padding))}";
    }
}
