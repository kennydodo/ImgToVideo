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

public sealed record ViewportResult(Rect Start, Rect End, IReadOnlyList<string> Warnings);

public sealed class MotionEngine
{
    private const double MarginFactor = 1.2;
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
            (ctx.ShotsSinceStatic >= _motion.StaticEveryMinShots &&
             ctx.Previous is MotionType.PanLeft or MotionType.PanRight))
        {
            return new MotionDecision(MotionType.Static, MotionSource.AutoSelected);
        }

        var preferred = ctx.Previous switch
        {
            MotionType.PanLeft => new[] { MotionType.Static, MotionType.PanRight, MotionType.ZoomIn },
            MotionType.PanRight => new[] { MotionType.Static, MotionType.PanLeft, MotionType.ZoomOut },
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

    public ViewportResult ComputeViewports(
        MotionType motion,
        MotionSource source,
        bool panRight,
        int imageWidth,
        int imageHeight)
    {
        var warnings = new List<string>();
        var aspect = (double)_output.Width / _output.Height;

        var viewportHeight = imageHeight / MarginFactor;
        var viewportWidth = viewportHeight * aspect;

        if (viewportWidth > imageWidth)
        {
            viewportWidth = imageWidth;
            viewportHeight = viewportWidth / aspect;
            warnings.Add(
                $"Image is narrower than the {MarginFactor:F1}x margin spec; viewport clamped to image width.");
        }

        if (viewportHeight > imageHeight)
        {
            viewportHeight = imageHeight;
            viewportWidth = viewportHeight * aspect;
            warnings.Add("Image is shorter than the margin spec; viewport clamped to image height.");
        }

        var minSize = MotionCodes.MinimumSize(motion);
        if (imageWidth < minSize.Width || imageHeight < minSize.Height)
        {
            warnings.Add(
                $"Image ({imageWidth}x{imageHeight}) is below the {minSize.Width}x{minSize.Height} " +
                $"spec for {motion}; movement range is limited.");
        }

        var result = motion switch
        {
            MotionType.Static => StaticViewports(viewportWidth, viewportHeight, imageWidth, imageHeight),
            MotionType.ZoomIn => ZoomViewports(
                _motion.PushInStartPercent, _motion.PushInEndPercent, viewportWidth, viewportHeight, imageWidth, imageHeight),
            MotionType.ZoomOut => ZoomViewports(
                _motion.ZoomOutStartPercent, _motion.ZoomOutEndPercent, viewportWidth, viewportHeight, imageWidth, imageHeight),
            _ => PanViewports(motion, source, panRight, viewportWidth, viewportHeight, imageWidth, imageHeight, warnings),
        };

        return new ViewportResult(result.Start, result.End, warnings);
    }

    private static ViewportResult StaticViewports(double vpW, double vpH, int imageWidth, int imageHeight)
    {
        var centered = Centered(vpW, vpH, imageWidth, imageHeight);
        return new ViewportResult(centered, centered, []);
    }

    private ViewportResult ZoomViewports(
        double startPercent, double endPercent, double vpW, double vpH, int imageWidth, int imageHeight)
    {
        var startSize = SizeAtScale(vpW, vpH, startPercent);
        var endSize = SizeAtScale(vpW, vpH, endPercent);
        return new ViewportResult(
            Centered(startSize.Width, startSize.Height, imageWidth, imageHeight),
            Centered(endSize.Width, endSize.Height, imageWidth, imageHeight),
            []);
    }

    private ViewportResult PanViewports(
        MotionType motion,
        MotionSource source,
        bool panRight,
        double vpW,
        double vpH,
        int imageWidth,
        int imageHeight,
        List<string> warnings)
    {
        var available = imageWidth - vpW;
        var y = (imageHeight - vpH) / 2.0;

        double startX;
        double endX;

        if (motion == MotionType.PanReveal)
        {
            startX = panRight ? 0 : available;
            endX = panRight ? available : 0;
        }
        else if (source == MotionSource.ExplicitCode)
        {
            startX = motion == MotionType.PanRight ? 0 : available;
            endX = motion == MotionType.PanRight ? available : 0;
        }
        else
        {
            var travel = Math.Min(_motion.PanMaxTravelPercent / 100.0 * _output.Width, available);
            var center = available / 2.0;
            if (motion == MotionType.PanRight)
            {
                startX = center - travel / 2.0;
                endX = center + travel / 2.0;
            }
            else
            {
                startX = center + travel / 2.0;
                endX = center - travel / 2.0;
            }
        }

        startX = Math.Clamp(startX, 0, Math.Max(0, available));
        endX = Math.Clamp(endX, 0, Math.Max(0, available));

        return new ViewportResult(
            new Rect(startX, y, vpW, vpH),
            new Rect(endX, y, vpW, vpH),
            warnings);
    }

    private static Rect Centered(double width, double height, int imageWidth, int imageHeight) =>
        new((imageWidth - width) / 2.0, (imageHeight - height) / 2.0, width, height);

    private static (double Width, double Height) SizeAtScale(double vpW, double vpH, double percent) =>
        (vpW * 100.0 / percent, vpH * 100.0 / percent);

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
