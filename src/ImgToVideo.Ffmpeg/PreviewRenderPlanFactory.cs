using System.Globalization;
using System.Text;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Ffmpeg;

public static class PreviewRenderPlanFactory
{
    private const long SupersampleThreshold = 3840;
    private const long SupersampleFactor = 2;

    public static RenderPlan Build(
        Timeline timeline,
        IReadOnlyList<ImageInfo> images,
        ProjectOptions options,
        string outputDirectory,
        string previewPath)
    {
        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        if (clips.Count == 0)
        {
            throw new InvalidOperationException("Timeline has no clips to render.");
        }

        var dimensions = images.ToDictionary(i => i.FilePath, i => (i.Width, i.Height));
        Directory.CreateDirectory(outputDirectory);

        var cuts = ComputeTransitionCuts(clips, options);
        var segments = new List<SegmentCommand>();
        long totalFrames = 0;
        long joinCount = 0;

        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (!dimensions.TryGetValue(clip.FilePath, out var size) || size.Width <= 0 || size.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"No usable dimensions for image \"{clip.FilePath}\"; cannot render.");
            }

            var (cutInFrames, _) = i > 0 ? cuts[i - 1] : (0L, TransitionKind.None);
            var (cutOutFrames, cutOutKind) = i < cuts.Length ? cuts[i] : (0L, TransitionKind.None);
            var pieceStart = HeadTrim(cutInFrames, options);
            var pieceEnd = clip.DurationFrames - TailTrim(cutOutFrames, options);
            var pieceCount = pieceEnd - pieceStart;

            var segmentPath = Path.Combine(outputDirectory, $"seg_{i + 1:D4}.mp4");
            segments.Add(new SegmentCommand(
                segments.Count + 1,
                segmentPath,
                BuildSegmentArguments(clip, size.Width, size.Height, pieceStart, pieceCount, options, segmentPath),
                pieceCount));
            totalFrames += pieceCount;

            if (cutOutFrames > 0)
            {
                var next = clips[i + 1];
                if (!dimensions.TryGetValue(next.FilePath, out var nextSize) ||
                    nextSize.Width <= 0 || nextSize.Height <= 0)
                {
                    throw new InvalidOperationException(
                        $"No usable dimensions for image \"{next.FilePath}\"; cannot render.");
                }

                joinCount++;
                var joinPath = Path.Combine(outputDirectory, $"join_{joinCount:D4}.mp4");
                segments.Add(new SegmentCommand(
                    segments.Count + 1,
                    joinPath,
                    BuildJoinArguments(
                        clip, size.Width, size.Height,
                        next, nextSize.Width, nextSize.Height,
                        cutOutFrames, cutOutKind, options, joinPath),
                    cutOutFrames));
                totalFrames += cutOutFrames;
            }
        }

        var roughPath = Path.Combine(outputDirectory, "rough.mp4");
        return new RenderPlan(
            Segments: segments,
            ConcatListPath: Path.Combine(outputDirectory, "concat.txt"),
            ConcatListContent: BuildConcatList(segments),
            ConcatArguments: BuildConcatArguments(Path.Combine(outputDirectory, "concat.txt"), roughPath),
            RoughPath: roughPath,
            MuxArguments: BuildMuxArguments(roughPath, timeline.Audio.FilePath, previewPath),
            PreviewPath: previewPath,
            TotalFrames: totalFrames);
    }

    private static (long Frames, TransitionKind Kind)[] ComputeTransitionCuts(
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
            var trim = (i > 0 ? TailTrim(cuts[i - 1].Frames, options) : 0) +
                       (i < cuts.Length ? HeadTrim(cuts[i].Frames, options) : 0);
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

    private static long TailTrim(long transitionFrames, ProjectOptions options) =>
        options.Transitions.Alignment == TransitionAlignment.Late
            ? transitionFrames
            : transitionFrames / 2;

    private static long HeadTrim(long transitionFrames, ProjectOptions options) =>
        options.Transitions.Alignment == TransitionAlignment.Late
            ? 0
            : (transitionFrames + 1) / 2;

    public static IReadOnlyList<string> BuildClipPreviewArguments(
        VideoClip clip, int sourceWidth, int sourceHeight, ProjectOptions options, string outputPath) =>
        BuildSegmentArguments(clip, sourceWidth, sourceHeight, 0, clip.DurationFrames, options, outputPath);

    public static IReadOnlyList<string> BuildClipPreviewMuxArguments(
        string videoPath, string audioPath, double startSeconds, double durationSeconds, string outputPath) =>
        new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", videoPath,
            "-ss", F(startSeconds),
            "-i", audioPath,
            "-t", F(durationSeconds),
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            outputPath,
        };

    private static IReadOnlyList<string> BuildSegmentArguments(
        VideoClip clip, int sourceWidth, int sourceHeight,
        long pieceStart, long pieceCount, ProjectOptions options, string outputPath)
    {
        if (pieceCount < 1)
        {
            throw new InvalidOperationException(
                $"Clip \"{clip.FilePath}\" has an empty render range; cannot render.");
        }

        return WrapSegmentArguments(
            new[] { "-i", clip.FilePath },
            null,
            BuildSideChain(clip, sourceWidth, sourceHeight, pieceStart, pieceCount, options),
            pieceCount, options.Render, outputPath);
    }

    private static IReadOnlyList<string> BuildJoinArguments(
        VideoClip outgoing, int outgoingWidth, int outgoingHeight,
        VideoClip incoming, int incomingWidth, int incomingHeight,
        long transitionFrames, TransitionKind kind, ProjectOptions options, string outputPath)
    {
        var outTail = TailTrim(transitionFrames, options);

        var outgoingChain = BuildSideChain(
            outgoing, outgoingWidth, outgoingHeight,
            pieceStart: outgoing.DurationFrames - outTail, pieceCount: transitionFrames, options);
        var incomingChain = BuildSideChain(
            incoming, incomingWidth, incomingHeight,
            pieceStart: -HeadTrim(transitionFrames, options), pieceCount: transitionFrames, options);

        var filterComplex =
            $"[0:v]{outgoingChain}[va];[1:v]{incomingChain}[vb];" +
            $"[va][vb]xfade=transition={TransitionCatalog.ToXfadeName(kind)}:" +
            $"duration={F(transitionFrames / (double)options.Output.Fps)}:offset=0," +
            "format=yuv420p";

        return WrapSegmentArguments(
            new[] { "-i", outgoing.FilePath, "-i", incoming.FilePath },
            filterComplex,
            null,
            transitionFrames, options.Render, outputPath);
    }

    private static string BuildSideChain(
        VideoClip clip, int sourceWidth, int sourceHeight,
        long pieceStart, long pieceCount, ProjectOptions options)
    {
        var outAspect = (double)options.Output.Width / options.Output.Height;
        var imageBounds = new Rect(0, 0, sourceWidth, sourceHeight);
        var startAspect = clip.StartViewport.Width / clip.StartViewport.Height;
        var endAspect = clip.EndViewport.Width / clip.EndViewport.Height;
        var viewportsMatchOutputAspect =
            Math.Abs(startAspect - outAspect) <= 0.02 && Math.Abs(endAspect - outAspect) <= 0.02;

        if (!viewportsMatchOutputAspect)
        {
            return $"scale={options.Render.PreviewWidth}:{options.Render.PreviewHeight}:" +
                   "force_original_aspect_ratio=decrease," +
                   $"pad={options.Render.PreviewWidth}:{options.Render.PreviewHeight}:(ow-iw)/2:(oh-ih)/2," +
                   "format=yuv420p";
        }

        var viewportsInsideImage =
            clip.StartViewport.IsInside(imageBounds) && clip.EndViewport.IsInside(imageBounds);

        if (viewportsInsideImage)
        {
            var supersample = sourceWidth < SupersampleThreshold ? SupersampleFactor : 1;
            var scaledWidth = sourceWidth * supersample;
            var scaledHeight = sourceHeight * supersample;
            var chain = supersample > 1
                ? $"scale={scaledWidth}:{scaledHeight}:flags=lanczos,"
                : string.Empty;
            return chain + BuildZoompanFilter(
                clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options) +
                ",format=yuv420p";
        }

        var canvasWidth = Even(Math.Max(
            Math.Max(clip.StartViewport.Right, clip.EndViewport.Right), sourceWidth));
        var canvasHeight = Even(Math.Max(
            Math.Max(clip.StartViewport.Bottom, clip.EndViewport.Bottom), sourceHeight));
        var supersampleFactor = canvasWidth < SupersampleThreshold ? SupersampleFactor : 1;
        var scaledCanvasWidth = canvasWidth * supersampleFactor;
        var scaledCanvasHeight = canvasHeight * supersampleFactor;
        var scaledImageWidth = sourceWidth * supersampleFactor;
        var scaledImageHeight = sourceHeight * supersampleFactor;

        return $"scale={scaledImageWidth}:{scaledImageHeight}:flags=lanczos," +
               $"pad={scaledCanvasWidth}:{scaledCanvasHeight}:(ow-iw)/2:(oh-ih)/2," +
               BuildZoompanFilter(
                   clip, canvasWidth, scaledCanvasWidth, scaledCanvasHeight, pieceStart, pieceCount, options) +
               ",format=yuv420p";
    }

    private static long Even(double value) =>
        (long)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2;

    private static IReadOnlyList<string> WrapSegmentArguments(
        IReadOnlyList<string> inputs,
        string? filterComplex,
        string? videoFilter,
        long frameCount,
        RenderOptions render,
        string outputPath)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
        };
        args.AddRange(inputs);

        if (filterComplex is not null)
        {
            args.Add("-filter_complex");
            args.Add(filterComplex);
        }
        else if (videoFilter is not null)
        {
            args.Add("-vf");
            args.Add(videoFilter);
        }

        args.Add("-frames:v");
        args.Add(frameCount.ToString(CultureInfo.InvariantCulture));
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add(render.PreviewPreset);
        args.Add("-crf");
        args.Add(render.PreviewCrf.ToString(CultureInfo.InvariantCulture));
        args.Add("-an");
        args.Add(outputPath);
        return args;
    }

    private static string BuildZoompanFilter(
        VideoClip clip, long sourceWidth, long scaledWidth, long scaledHeight,
        long pieceStart, long pieceCount, ProjectOptions options)
    {
        var clipDuration = clip.DurationFrames;
        var steps = clipDuration > 1 ? clipDuration - 1 : 1;
        var supersample = (double)scaledWidth / sourceWidth;

        var widthStart = supersample * clip.StartViewport.Width;
        var widthEnd = supersample * clip.EndViewport.Width;
        var centerXStart = supersample * (clip.StartViewport.X + clip.StartViewport.Width / 2.0);
        var centerXEnd = supersample * (clip.EndViewport.X + clip.EndViewport.Width / 2.0);
        var centerYStart = supersample * (clip.StartViewport.Y + clip.StartViewport.Height / 2.0);
        var centerYEnd = supersample * (clip.EndViewport.Y + clip.EndViewport.Height / 2.0);

        var zoomHigh = Math.Max(sourceWidth / clip.StartViewport.Width, sourceWidth / clip.EndViewport.Width);
        var zoomLow = Math.Min(sourceWidth / clip.StartViewport.Width, sourceWidth / clip.EndViewport.Width);

        string zoomExpression;
        string xExpression;
        string yExpression;
        if (pieceCount <= 1)
        {
            var progress = EasingValue(clip.Easing, clipDuration > 1 ? (double)pieceStart / steps : 0.0);
            var width = clip.StartViewport.Width + (clip.EndViewport.Width - clip.StartViewport.Width) * progress;
            var centerX = (clip.StartViewport.X + clip.StartViewport.Width / 2.0) +
                          ((clip.EndViewport.X - clip.StartViewport.X) * progress);
            var centerY = (clip.StartViewport.Y + clip.StartViewport.Height / 2.0) +
                          ((clip.EndViewport.Y - clip.StartViewport.Y) * progress);
            var height = clip.StartViewport.Height + (clip.EndViewport.Height - clip.StartViewport.Height) * progress;
            var zoom = sourceWidth / width;

            zoomExpression = F(zoom);
            xExpression = F(supersample * centerX - scaledWidth / zoom / 2.0);
            yExpression = F(supersample * centerY - scaledHeight / zoom / 2.0);
        }
        else
        {
            var progress = EasingExpression(clip.Easing, $"((on{SignedOffset(pieceStart)})/{steps})");

            zoomExpression =
                $"min({F(zoomHigh)},max({F(zoomLow)},{F(scaledWidth)}/({F(widthStart)}+({F(widthEnd - widthStart)})*{progress})))";
            xExpression =
                $"min(max(0,({F(centerXStart)}+({F(centerXEnd - centerXStart)})*{progress})-iw/zoom/2),iw-iw/zoom)";
            yExpression =
                $"min(max(0,({F(centerYStart)}+({F(centerYEnd - centerYStart)})*{progress})-ih/zoom/2),ih-ih/zoom)";
        }

        return $"zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}'" +
               $":d={pieceCount.ToString(CultureInfo.InvariantCulture)}" +
               $":s={options.Render.PreviewWidth}x{options.Render.PreviewHeight}" +
               $":fps={F(options.Output.Fps)}";
    }

    private static string EasingExpression(EasingMode easing, string progress)
    {
        var clamped = $"min(1,max(0,{progress}))";
        return easing switch
        {
            EasingMode.EaseIn => $"pow({clamped},2)",
            EasingMode.EaseOut => $"1-pow(1-{clamped},2)",
            EasingMode.EaseInOut => $"pow({clamped},3)*({clamped}*({clamped}*6-15)+10)",
            _ => progress,
        };
    }

    private static double EasingValue(EasingMode easing, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return easing switch
        {
            EasingMode.EaseIn => progress * progress,
            EasingMode.EaseOut => 1 - (1 - progress) * (1 - progress),
            EasingMode.EaseInOut => progress * progress * progress * (progress * (progress * 6 - 15) + 10),
            _ => progress,
        };
    }

    private static string SignedOffset(long pieceStart) =>
        pieceStart < 0
            ? pieceStart.ToString(CultureInfo.InvariantCulture)
            : "+" + pieceStart.ToString(CultureInfo.InvariantCulture);

    private static string BuildConcatList(IReadOnlyList<SegmentCommand> segments)
    {
        var builder = new StringBuilder("ffconcat version 1.0\n");
        foreach (var segment in segments)
        {
            builder.Append("file '").Append(segment.OutputPath.Replace("'", "'\\''")).Append("'\n");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> BuildConcatArguments(string concatListPath, string roughPath) =>
        new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatListPath,
            "-c", "copy",
            roughPath,
        };

    private static IReadOnlyList<string> BuildMuxArguments(
        string roughPath, string audioPath, string previewPath) =>
        new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", roughPath,
            "-i", audioPath,
            "-map", "0:v:0",
            "-map", "1:a:0",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            previewPath,
        };

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
