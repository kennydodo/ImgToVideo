namespace ImgToVideo.Core.Models;

public enum TransitionAlignment
{
    Centered,
    Late,
}

public static class TransitionAlignments
{
    public static IReadOnlyList<(TransitionAlignment Alignment, string Name)> All => new[]
    {
        (TransitionAlignment.Centered, "Centered on cut"),
        (TransitionAlignment.Late, "Start at cut"),
    };

    public static string? NameOf(TransitionAlignment alignment) =>
        All.FirstOrDefault(a => a.Alignment == alignment).Name;

    public static bool TryFromName(string name, out TransitionAlignment alignment)
    {
        foreach (var (a, n) in All)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                alignment = a;
                return true;
            }
        }

        alignment = default;
        return false;
    }
}
