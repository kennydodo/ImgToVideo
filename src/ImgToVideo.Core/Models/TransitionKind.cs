namespace ImgToVideo.Core.Models;

public enum TransitionKind
{
    None,
    Crossfade,
    FadeBlack,
    FadeWhite,
    WipeLeft,
    WipeRight,
    WipeUp,
    WipeDown,
    SlideLeft,
    SlideRight,
    Dissolve,
    CircleOpen,
    CircleClose,
    SmoothLeft,
    SmoothRight,
}

public static class TransitionCatalog
{
    public static IReadOnlyList<(TransitionKind Kind, string Name)> Shortlist => new[]
    {
        (TransitionKind.Crossfade, "Crossfade"),
        (TransitionKind.FadeBlack, "Dip to black"),
        (TransitionKind.FadeWhite, "Dip to white"),
        (TransitionKind.WipeLeft, "Wipe left"),
        (TransitionKind.WipeRight, "Wipe right"),
        (TransitionKind.WipeUp, "Wipe up"),
        (TransitionKind.WipeDown, "Wipe down"),
        (TransitionKind.SlideLeft, "Slide left"),
        (TransitionKind.SlideRight, "Slide right"),
        (TransitionKind.Dissolve, "Dissolve"),
        (TransitionKind.CircleOpen, "Circle open"),
        (TransitionKind.CircleClose, "Circle close"),
        (TransitionKind.SmoothLeft, "Smooth push left"),
        (TransitionKind.SmoothRight, "Smooth push right"),
    };

    public static string? NameOf(TransitionKind kind) =>
        Shortlist.FirstOrDefault(t => t.Kind == kind).Name;

    public static bool TryFromName(string name, out TransitionKind kind)
    {
        foreach (var (k, n) in Shortlist)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                kind = k;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string ToXfadeName(TransitionKind kind) => kind switch
    {
        TransitionKind.Crossfade => "fade",
        TransitionKind.FadeBlack => "fadeblack",
        TransitionKind.FadeWhite => "fadewhite",
        TransitionKind.WipeLeft => "wipeleft",
        TransitionKind.WipeRight => "wiperight",
        TransitionKind.WipeUp => "wipeup",
        TransitionKind.WipeDown => "wipedown",
        TransitionKind.SlideLeft => "slideleft",
        TransitionKind.SlideRight => "slideright",
        TransitionKind.Dissolve => "dissolve",
        TransitionKind.CircleOpen => "circleopen",
        TransitionKind.CircleClose => "circleclose",
        TransitionKind.SmoothLeft => "smoothleft",
        TransitionKind.SmoothRight => "smoothright",
        _ => "fade",
    };
}
