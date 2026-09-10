namespace ImgToVideo.Core.Models;

public enum ImageType
{
    Scene,
    CloseUp,
    Infographic,
    Comparison,
    Process,
    Hybrid,
    Overview,
}

public static class ImageTypes
{
    public static readonly IReadOnlyList<(string Code, ImageType Type)> All = new[]
    {
        ("SCN", ImageType.Scene),
        ("CU", ImageType.CloseUp),
        ("INF", ImageType.Infographic),
        ("CMP", ImageType.Comparison),
        ("PROC", ImageType.Process),
        ("HYB", ImageType.Hybrid),
        ("OVR", ImageType.Overview),
    };

    public static bool IsKnownCode(string code) => TryFromCode(code, out _);

    public static bool TryFromCode(string code, out ImageType type)
    {
        foreach (var (c, t) in All)
        {
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
            {
                type = t;
                return true;
            }
        }

        type = default;
        return false;
    }
}
