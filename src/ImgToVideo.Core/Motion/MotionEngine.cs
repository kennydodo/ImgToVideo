using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Motion;

public sealed record MotionContext(
    MotionType? ExplicitCode,
    bool IsVideoStart,
    bool IsVideoEnd,
    bool IsSceneStart,
    MotionType? Previous,
    int ShotsSinceStatic);

public sealed record MotionDecision(MotionType Motion, MotionSource Source);

public sealed record ClipMotionPlan(
    MotionType Motion,
    MotionSource Source,
    Rect Start,
    Rect End,
    IReadOnlyList<string> Warnings);

public sealed class MotionEngine
{
    private const double MinTravelPixels = 32;
    private static readonly MotionType[] AutoPool = [MotionType.Static, MotionType.ZoomIn, MotionType.ZoomOut, MotionType.PanLeft, MotionType.PanRight];

    private readonly MotionOptions _motion;
    private readonly OutputOptions _output;

    public MotionEngine(MotionOptions motion, OutputOptions output)
    {
        _motion = motion;
        _output = output;
    }

    public MotionDecision SelectMotion(MotionContext ctx)
    {
        if (ctx.ExplicitCode is { } explicitCode)
        {
            return new MotionDecision(explicitCode, MotionSource.ExplicitCode);
        }

        if (ctx.IsVideoStart)
        {
            return new MotionDecision(MotionType.ZoomIn, MotionSource.AutoSelected);
        }

        if (ctx.IsVideoEnd)
        {
            var closing = PickFirstNotPrevious([MotionType.Static, MotionType.ZoomOut], ctx.Previous);
            return new MotionDecision(closing, MotionSource.AutoSelected);
        }

        if (ctx.IsSceneStart)
        {
            var opening = PickFirstNotPrevious([MotionType.ZoomIn, MotionType.Static], ctx.Previous);
            return new MotionDecision(opening, MotionSource.AutoSelected);
        }

        if (ctx.ShotsSinceStatic >= Math.Max(1, _motion.StaticEveryMaxShots) ||
            (ctx.ShotsSinceStatic >= _motion.StaticEveryMinShots && IsPan(ctx.Previous)))
        {
            return new MotionDecision(MotionType.Static, MotionSource.AutoSelected);
        }

        var preferred = ctx.Previous switch
        {
            MotionType.PanLeft => new[] { MotionType.Static, MotionType.PanRight, MotionType.ZoomIn },
            MotionType.PanRight => new[] { MotionType.Static, MotionType.PanLeft, MotionType.ZoomOut },
            MotionType.PanUp => new[] { MotionType.Static, MotionType.ZoomIn, MotionType.ZoomOut },
            MotionType.PanDown => new[] { MotionType.Static, MotionType.ZoomIn, MotionType.ZoomOut },
            MotionType.ZoomIn => new[] { MotionType.Static, MotionType.PanRight, MotionType.ZoomOut },
            MotionType.ZoomOut => new[] { MotionType.Static, MotionType.ZoomIn, MotionType.PanLeft },
            _ => new[] { MotionType.ZoomIn, MotionType.PanLeft, MotionType.PanRight, MotionType.ZoomOut },
        };

        foreach (var candidate in preferred)
        {
            if (AutoPool.Contains(candidate) && candidate != ctx.Previous)
            {
                return new MotionDecision(candidate, MotionSource.AutoSelected);
            }
        }

        return new MotionDecision(PickFirstNotPrevious(AutoPool, ctx.Previous), MotionSource.AutoSelected);
    }

    public ClipMotionPlan PlanClip(
        MotionType motion,
        MotionSource source,
        bool panRight,
        int imageWidth,
        int imageHeight)
    {
        var warnings = new List<string>();
        var outAspect = (double)_output.Width / _output.Height;
        var imageAspect = (double)imageWidth / imageHeight;
        var fullRect = new Rect(0, 0, imageWidth, imageHeight);

        var fitWidth = imageAspect > outAspect ? imageHeight * outAspect : imageWidth;
        var fitHeight = imageAspect > outAspect ? imageHeight : imageWidth / outAspect;
        var spareX = imageWidth - fitWidth;
        var spareY = imageHeight - fitHeight;

        switch (motion)
        {
            case MotionType.Static:
                return new ClipMotionPlan(motion, source, fullRect, fullRect, warnings);

            case MotionType.ZoomIn:
            case MotionType.ZoomOut:
            {
                var baseRect = CanvasRect(imageWidth, imageHeight, outAspect);
                var startPercent = motion == MotionType.ZoomIn
                    ? _motion.PushInStartPercent
                    : _motion.ZoomOutStartPercent;
                var endPercent = motion == MotionType.ZoomIn
                    ? _motion.PushInEndPercent
                    : _motion.ZoomOutEndPercent;

                var start = ShrinkAroundCenter(baseRect, 100.0 / startPercent);
                var end = ShrinkAroundCenter(baseRect, 100.0 / endPercent);
                if (motion == MotionType.ZoomOut)
                {
                    (start, end) = (end, start);
                }

                return new ClipMotionPlan(motion, source, start, end, warnings);
            }

            case MotionType.PanLeft:
            case MotionType.PanRight:
            case MotionType.PanReveal:
            {
                if (spareX < MinTravelPixels)
                {
                    return PushInFallback(
                        $"No horizontal overscan for {motion} (image {imageWidth}x{imageHeight}); " +
                        "using a gentle push-in instead — generate a 150%-wide image (2880x1296) to pan.",
                        panRight, imageWidth, imageHeight);
                }

                var y = (imageHeight - fitHeight) / 2.0;
                double startX;
                double endX;
                if (motion == MotionType.PanReveal)
                {
                    startX = panRight ? 0 : spareX;
                    endX = panRight ? spareX : 0;
                }
                else if (source == MotionSource.ExplicitCode)
                {
                    startX = motion == MotionType.PanRight ? 0 : spareX;
                    endX = motion == MotionType.PanRight ? spareX : 0;
                }
                else
                {
                    var capped = Math.Min(_motion.PanMaxTravelPercent / 100.0 * _output.Width, spareX);
                    var center = spareX / 2.0;
                    if (motion == MotionType.PanRight)
                    {
                        startX = center - capped / 2.0;
                        endX = center + capped / 2.0;
                    }
                    else
                    {
                        startX = center + capped / 2.0;
                        endX = center - capped / 2.0;
                    }
                }

                return new ClipMotionPlan(
                    motion, source,
                    new Rect(startX, y, fitWidth, fitHeight),
                    new Rect(endX, y, fitWidth, fitHeight),
                    warnings);
            }

            case MotionType.PanUp:
            case MotionType.PanDown:
            {
                if (spareY < MinTravelPixels)
                {
                    return PushInFallback(
                        $"No vertical overscan for {motion} (image {imageWidth}x{imageHeight}); " +
                        "using a gentle push-in instead — generate a 200%-tall image (2304x2160) to pan.",
                        panRight, imageWidth, imageHeight);
                }

                var x = (imageWidth - fitWidth) / 2.0;
                double startY;
                double endY;
                if (motion == MotionType.PanUp)
                {
                    startY = spareY;
                    endY = 0;
                }
                else
                {
                    startY = 0;
                    endY = spareY;
                }

                return new ClipMotionPlan(
                    motion, source,
                    new Rect(x, startY, fitWidth, fitHeight),
                    new Rect(x, endY, fitWidth, fitHeight),
                    warnings);
            }

            default:
                return new ClipMotionPlan(motion, source, fullRect, fullRect, warnings);
        }
    }

    private ClipMotionPlan PushInFallback(
        string warning, bool panRight, int imageWidth, int imageHeight)
    {
        var fallback = PlanClip(
            MotionType.ZoomIn, MotionSource.AutoSelected, panRight, imageWidth, imageHeight);
        var warnings = new List<string> { warning };
        warnings.AddRange(fallback.Warnings);
        return fallback with { Warnings = warnings };
    }

    private static bool IsPan(MotionType? motion) =>
        motion is MotionType.PanLeft or MotionType.PanRight or MotionType.PanUp or MotionType.PanDown;

    private static Rect CanvasRect(int imageWidth, int imageHeight, double outAspect)
    {
        long canvasWidth;
        long canvasHeight;
        if ((double)imageWidth / imageHeight < outAspect)
        {
            canvasHeight = imageHeight;
            canvasWidth = Even(imageHeight * outAspect);
        }
        else
        {
            canvasWidth = imageWidth;
            canvasHeight = Even(imageWidth / outAspect);
        }

        return new Rect((canvasWidth - imageWidth) / 2.0, (canvasHeight - imageHeight) / 2.0, canvasWidth, canvasHeight);
    }

    private static Rect ShrinkAroundCenter(Rect rect, double factor) =>
        new(
            rect.X + rect.Width * (1 - factor) / 2.0,
            rect.Y + rect.Height * (1 - factor) / 2.0,
            rect.Width * factor,
            rect.Height * factor);

    private static long Even(double value) =>
        (long)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2;

    private static MotionType PickFirstNotPrevious(IReadOnlyList<MotionType> candidates, MotionType? previous)
    {
        foreach (var candidate in candidates)
        {
            if (candidate != previous)
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
