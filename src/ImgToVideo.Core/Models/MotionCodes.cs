namespace ImgToVideo.Core.Models;

public static class MotionCodes
{
    public static readonly IReadOnlyList<(string Suffix, MotionType Motion)> All = new[]
    {
        ("ST", MotionType.Static),
        ("ZI", MotionType.ZoomIn),
        ("ZO", MotionType.ZoomOut),
        ("PL", MotionType.PanLeft),
        ("PR", MotionType.PanRight),
        ("PV", MotionType.PanReveal),
    };

    public static bool TryFromSuffix(string suffix, out MotionType motion)
    {
        foreach (var (s, m) in All)
        {
            if (string.Equals(s, suffix, StringComparison.OrdinalIgnoreCase))
            {
                motion = m;
                return true;
            }
        }

        motion = default;
        return false;
    }

    public static bool IsKnownSuffix(string suffix) => TryFromSuffix(suffix, out _);

    public static (int Width, int Height) MinimumSize(MotionType motion) => motion switch
    {
        MotionType.PanLeft or MotionType.PanRight => (2880, 1296),
        MotionType.PanReveal => (3840, 1296),
        _ => (2304, 1296),
    };
}
