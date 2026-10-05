namespace ImgToVideo.Core.Models;

/// <summary>
/// A reveal shot is ONE generated image holding 2-4 items in a left-to-right
/// row, each item in its own equal-width slice. The video shows the slices one
/// at a time as the narrator reaches each item: the planner turns the shot
/// into consecutive clips whose images show slices 1..k (the rest black).
/// Static only - there is no motion on a reveal shot.
/// </summary>
public static class RevealLimits
{
    public const int MinItems = 2;
    public const int MaxItems = 4;
}
