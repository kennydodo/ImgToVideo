namespace ImgToVideo.Core.Models;

public enum EasingMode
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
}

public static class EasingModes
{
    public static IReadOnlyList<(EasingMode Mode, string Name)> All => new[]
    {
        (EasingMode.Linear, "Linear"),
        (EasingMode.EaseIn, "Ease in"),
        (EasingMode.EaseOut, "Ease out"),
        (EasingMode.EaseInOut, "Ease in out"),
    };

    public static string? NameOf(EasingMode mode) =>
        All.FirstOrDefault(e => e.Mode == mode).Name;

    public static bool TryFromName(string name, out EasingMode mode)
    {
        foreach (var (m, n) in All)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                mode = m;
                return true;
            }
        }

        mode = default;
        return false;
    }
}
