using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Planning;

/// <summary>
/// Single source of truth for where a transition sits around its cut, shared
/// by the ffmpeg render plan and the Premiere exporter so the NLE timeline
/// mirrors the rendered video. "Late" (the default, labeled "Start at cut")
/// runs the whole transition at and after the cut, so an incoming clip never
/// appears before its cue - the pop sound and the image entering coincide.
/// "Centered" splits the fade across the cut instead.
/// </summary>
public static class TransitionCuts
{
    public static long TailTrim(long transitionFrames, ProjectOptions options) =>
        options.Transitions.Alignment == TransitionAlignment.Late
            ? 0
            : transitionFrames / 2;

    public static long HeadTrim(long transitionFrames, ProjectOptions options) =>
        options.Transitions.Alignment == TransitionAlignment.Late
            ? transitionFrames
            : (transitionFrames + 1) / 2;

    /// <summary>The effective transition per cut (0/None for a hard cut).</summary>
    public static (long Frames, TransitionKind Kind)[] Compute(
        IReadOnlyList<VideoClip> clips, ProjectOptions options)
    {
        var cuts = new (long Frames, TransitionKind Kind)[Math.Max(0, clips.Count - 1)];

        for (var c = 0; c < cuts.Length; c++)
        {
            var transition = clips[c + 1].Transition;
            if (transition is not { Kind: not TransitionKind.None, DurationFrames: >= 2 })
            {
                continue;
            }

            var tail = TailTrim(transition.DurationFrames, options);
            var head = HeadTrim(transition.DurationFrames, options);
            if (clips[c].DurationFrames - tail < 1 || clips[c + 1].DurationFrames - head < 1)
            {
                continue;
            }

            cuts[c] = (transition.DurationFrames, transition.Kind);
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var trim = (i > 0 ? HeadTrim(cuts[i - 1].Frames, options) : 0) +
                       (i < cuts.Length ? TailTrim(cuts[i].Frames, options) : 0);
            if (clips[i].DurationFrames - trim < 1)
            {
                if (i > 0)
                {
                    cuts[i - 1] = (0, TransitionKind.None);
                }

                if (i < cuts.Length)
                {
                    cuts[i] = (0, TransitionKind.None);
                }
            }
        }

        return cuts;
    }
}
