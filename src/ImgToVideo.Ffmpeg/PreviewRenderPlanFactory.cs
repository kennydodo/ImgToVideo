using System.Globalization;
using System.Text;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Ffmpeg;

public static class PreviewRenderPlanFactory
{
    /// <summary>Supersample factor cap: the crop/pan input memory and CPU cost
    /// scale with the square of the factor.</summary>
    private const long SupersampleMaxFactor = 8;

    /// <summary>The crop/pan input grid is never stretched beyond this width,
    /// so memory and CPU stay bounded for huge sources.</summary>
    private const long SupersampleMaxGridWidth = 16384;

    /// <summary>The pan/zoom grid is kept at least this multiple of the render
    /// width: the crop rounding is only invisible when the grid is a large
    /// multiple of the size the viewer actually sees.</summary>
    private const double PreviewToGridRatio = 4.8;

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

    public static long TailTrimFrames(long transitionFrames, ProjectOptions options) =>
        TailTrim(transitionFrames, options);

    public static long HeadTrimFrames(long transitionFrames, ProjectOptions options) =>
        HeadTrim(transitionFrames, options);

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
            SourceInputArguments(clip.FilePath, options),
            null,
            BuildSideChain(clip, sourceWidth, sourceHeight, pieceStart, pieceCount, options),
            pieceCount, options.Render, outputPath);
    }

    /// <summary>
    /// Opens a still-image source clip as an infinite, correctly-timed stream:
    /// "-loop 1" repeats the single decoded frame indefinitely and "-r" stamps
    /// it at the project's frame rate, so downstream filters (crop, in
    /// particular - see BuildCropPanFilter) see a plain sequential frame
    /// stream at the right pace, without needing their own frame-duplication
    /// or retiming logic. Trimmed to exactly the frames each segment needs by
    /// the "-frames:v" already applied in WrapSegmentArguments.
    /// </summary>
    private static IReadOnlyList<string> SourceInputArguments(string filePath, ProjectOptions options) =>
        new[] { "-loop", "1", "-r", F(options.Output.Fps), "-i", filePath };

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
            SourceInputArguments(outgoing.FilePath, options)
                .Concat(SourceInputArguments(incoming.FilePath, options))
                .ToList(),
            filterComplex,
            null,
            transitionFrames, options.Render, outputPath);
    }

    /// <summary>
    /// Supersample factor so the crop/pan input grid reaches roughly
    /// <paramref name="targetWidth"/>. crop rounds its window to whole input
    /// pixels, so a slow pan/zoom moves in (out_w / grid) pixel steps - and the
    /// player stretches the file to the screen, so the on-screen wobble is
    /// screenWidth / grid regardless of the file resolution. The old fixed 2x
    /// left a 720p source on a 2752 grid (a visible period-2 stair-step) and
    /// today leaves a 2K source on a 5120 grid: still a visible half-pixel
    /// wobble fullscreen. The factor is capped both by
    /// <see cref="SupersampleMaxFactor"/> and by the grid-width guard so
    /// memory stays sane. 0 targetWidth disables supersampling (fastest, but
    /// jittery on slow motions).
    /// </summary>
    public static int SupersampleFor(long sourceWidth, int targetWidth)
    {
        if (targetWidth <= 0 || sourceWidth >= targetWidth)
        {
            return 1;
        }

        var gridCap = (int)Math.Min(SupersampleMaxFactor, SupersampleMaxGridWidth / sourceWidth);
        if (gridCap < 2)
        {
            return 1;
        }

        var factor = (int)Math.Ceiling(targetWidth / (double)sourceWidth);
        return Math.Clamp(factor, 2, gridCap);
    }

    /// <summary>
    /// Effective crop/pan grid target for the current render: the configured
    /// absolute width, but never below ~4.8x the render width. A target tuned
    /// at one render size (4608 for the 960 preview) leaves larger renders -
    /// e.g. the 2560-wide final - on a grid too coarse for smooth motion.
    /// 0 keeps supersampling disabled.
    /// </summary>
    public static long SupersampleTargetFor(ProjectOptions options) =>
        options.Render.SupersampleTargetWidth <= 0
            ? 0
            : Math.Max(options.Render.SupersampleTargetWidth,
                       (long)Math.Round(options.Render.PreviewWidth * PreviewToGridRatio));

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
            var supersample = SupersampleFor(sourceWidth, (int)SupersampleTargetFor(options));
            var scaledWidth = sourceWidth * supersample;
            var scaledHeight = sourceHeight * supersample;
            var chain = supersample > 1
                ? $"scale={scaledWidth}:{scaledHeight}:flags=lanczos,"
                : string.Empty;
            return chain + BuildCropPanFilter(
                clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options) +
                ",format=yuv420p";
        }

        var canvasWidth = Even(Math.Max(
            Math.Max(clip.StartViewport.Right, clip.EndViewport.Right), sourceWidth));
        var canvasHeight = Even(Math.Max(
            Math.Max(clip.StartViewport.Bottom, clip.EndViewport.Bottom), sourceHeight));
        var supersampleFactor = SupersampleFor(canvasWidth, (int)SupersampleTargetFor(options));
        var scaledCanvasWidth = canvasWidth * supersampleFactor;
        var scaledCanvasHeight = canvasHeight * supersampleFactor;
        var scaledImageWidth = sourceWidth * supersampleFactor;
        var scaledImageHeight = sourceHeight * supersampleFactor;

        return $"scale={scaledImageWidth}:{scaledImageHeight}:flags=lanczos," +
               $"pad={scaledCanvasWidth}:{scaledCanvasHeight}:(ow-iw)/2:(oh-ih)/2," +
               BuildCropPanFilter(
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
        args.AddRange(VideoEncoderArgs(render.Encoder, render.PreviewPreset,
                                       render.PreviewCrf, render.PreviewBframes));
        args.Add("-an");
        args.Add(outputPath);
        return args;
    }

    /// <summary>
    /// Resolves the configured encoder to a concrete ffmpeg encoder name, falling
    /// back to CPU libx264 when a hardware encoder is requested but unavailable.
    /// </summary>
    public static string ResolveEncoder(RenderOptions render)
    {
        var configured = (render.Encoder ?? "auto").Trim().ToLowerInvariant();
        if (configured is "libx264" or "cpu")
        {
            return "libx264";
        }

        var candidates = configured switch
        {
            "h264_nvenc" or "nvenc" => ["h264_nvenc"],
            "h264_amf" or "amf" => ["h264_amf"],
            "h264_qsv" or "qsv" => ["h264_qsv"],
            _ => (IReadOnlyList<string>)["h264_nvenc", "h264_amf", "h264_qsv"],
        };

        var available = EncoderProbe.GetEncoders(render.FfmpegPath);
        foreach (var candidate in candidates)
        {
            if (available.Contains(candidate))
            {
                return candidate;
            }
        }

        return "libx264";
    }

    /// <summary>Pure mapping of encoder + libx264-style preset/CRF onto encoder arguments.</summary>
    public static IReadOnlyList<string> VideoEncoderArgs(string encoder, string preset, int crf,
                                                         int bframes = 3)
    {
        var crfText = crf.ToString(CultureInfo.InvariantCulture);
        var bf = bframes.ToString(CultureInfo.InvariantCulture);
        return encoder switch
        {
            "h264_nvenc" =>
            [
                "-c:v", "h264_nvenc",
                "-preset", NvencPreset(preset),
                "-rc", "vbr",
                "-cq", crfText,
                "-b:v", "0",
                "-bf", bf,
            ],
            "h264_qsv" =>
            [
                "-c:v", "h264_qsv",
                "-preset", QsvPreset(preset),
                "-global_quality", crfText,
                "-bf", bf,
            ],
            "h264_amf" =>
            [
                "-c:v", "h264_amf",
                "-quality", AmfQuality(preset),
                "-rc", "cqp",
                "-qp_i", crfText,
                "-qp_p", crfText,
                "-bf", bf,
            ],
            _ =>
            [
                "-c:v", "libx264",
                "-preset", preset,
                "-crf", crfText,
                "-bf", bf,
            ],
        };
    }

    private static string NvencPreset(string preset) => preset.ToLowerInvariant() switch
    {
        "ultrafast" => "p1",
        "superfast" => "p2",
        "veryfast" => "p3",
        "faster" => "p4",
        "fast" => "p4",
        "medium" => "p5",
        "slow" => "p6",
        "slower" => "p7",
        "veryslow" => "p7",
        _ => "p5",
    };

    private static string QsvPreset(string preset) => preset.ToLowerInvariant() switch
    {
        "ultrafast" or "superfast" or "veryfast" or "faster" or "fast" => preset.ToLowerInvariant(),
        "slow" or "slower" or "veryslow" => preset.ToLowerInvariant(),
        _ => "medium",
    };

    private static string AmfQuality(string preset) => preset.ToLowerInvariant() switch
    {
        "ultrafast" or "superfast" or "veryfast" or "faster" or "fast" => "speed",
        "slow" or "slower" or "veryslow" => "quality",
        _ => "balanced",
    };

    /// <summary>
    /// Builds the pan/zoom stage of the side chain as an explicit, independently
    /// sized <c>crop</c> (width, height, x and y each interpolated on their own
    /// between <see cref="VideoClip.StartViewport"/> and
    /// <see cref="VideoClip.EndViewport"/>) followed by a plain resize to the
    /// render's preview size.
    ///
    /// This replaces an earlier <c>zoompan</c>-based implementation. zoompan's
    /// <c>z</c> (zoom) parameter always crops <c>iw/z</c> by <c>ih/z</c> - the
    /// SAME ratio on both axes as the source frame - so it can only ever produce
    /// a crop window with the source's own aspect ratio. That is fine for
    /// Zoom In/Out (which shrink evenly on both axes) and incidentally close
    /// enough for Pan Left/Right (which move only in X), but it cannot express
    /// Pan Up/Down at all: those only change Y, so the single shared zoom value
    /// stays fixed at 1 for the whole clip, which forces zoompan's y-clamp
    /// bound (<c>ih-ih/zoom</c>) to exactly 0 and the pan never moves. The same
    /// coupling also over-crops Pan Left/Right vertically whenever the source
    /// isn't already exactly the output's aspect ratio. Since
    /// <see cref="VideoClip.StartViewport"/>/<see cref="VideoClip.EndViewport"/>
    /// are always constructed (see MotionEngine.PlanClip) to already match the
    /// output aspect ratio, an explicit crop needs no zoom coupling at all: each
    /// axis is interpolated independently, and the trailing scale is a clean,
    /// non-distorting resize because the crop's aspect always matches
    /// PreviewWidth:PreviewHeight already.
    ///
    /// Because <c>crop</c> (unlike <c>zoompan</c>) does not itself expand a
    /// single input frame into <paramref name="pieceCount"/> output frames,
    /// the source is opened with "-loop 1 -r fps" (see
    /// <see cref="SourceInputArguments"/>) so it already arrives here as a
    /// correctly-timed, indefinitely repeating stream; crop's own per-frame
    /// <c>n</c> variable then plays the same role zoompan's "on" did.
    /// </summary>
    private static string BuildCropPanFilter(
        VideoClip clip, long sourceWidth, long scaledWidth, long scaledHeight,
        long pieceStart, long pieceCount, ProjectOptions options)
    {
        var clipDuration = clip.DurationFrames;
        var steps = clipDuration > 1 ? clipDuration - 1 : 1;
        var motionSteps = clip.MotionDurationFrames is { } motionEnd && motionEnd > 1
            ? Math.Max(1, Math.Min(motionEnd - 1, steps))
            : steps;
        var supersample = (double)scaledWidth / sourceWidth;

        var widthStart = supersample * clip.StartViewport.Width;
        var widthEnd = supersample * clip.EndViewport.Width;
        var heightStart = supersample * clip.StartViewport.Height;
        var heightEnd = supersample * clip.EndViewport.Height;
        var xStart = supersample * clip.StartViewport.X;
        var xEnd = supersample * clip.EndViewport.X;
        var yStart = supersample * clip.StartViewport.Y;
        var yEnd = supersample * clip.EndViewport.Y;

        string widthExpression;
        string heightExpression;
        string xExpression;
        string yExpression;
        if (pieceCount <= 1)
        {
            var progress = EasingValue(clip.Easing, clipDuration > 1 ? (double)pieceStart / motionSteps : 0.0);
            var width = widthStart + (widthEnd - widthStart) * progress;
            var height = heightStart + (heightEnd - heightStart) * progress;
            var x = xStart + (xEnd - xStart) * progress;
            var y = yStart + (yEnd - yStart) * progress;

            widthExpression = F(width);
            heightExpression = F(height);
            xExpression = F(x);
            yExpression = F(y);
        }
        else
        {
            var rawProgress = $"((n{SignedOffset(pieceStart)})/{motionSteps})";
            if (motionSteps != steps)
            {
                // The move completes after motionSteps frames and the framing holds.
                rawProgress = $"min(1,{rawProgress})";
            }

            var progress = EasingExpression(clip.Easing, rawProgress);

            // Each axis is interpolated on its own - no shared zoom value, so
            // width/height and x/y never have to fight each other's aspect ratio.
            widthExpression = $"min(iw,max(1,{F(widthStart)}+({F(widthEnd - widthStart)})*{progress}))";
            heightExpression = $"min(ih,max(1,{F(heightStart)}+({F(heightEnd - heightStart)})*{progress}))";
            xExpression = $"min(iw-ow,max(0,{F(xStart)}+({F(xEnd - xStart)})*{progress}))";
            yExpression = $"min(ih-oh,max(0,{F(yStart)}+({F(yEnd - yStart)})*{progress}))";
        }

        // The input is opened with "-loop 1 -r <fps>" (see BuildSegmentArguments/
        // BuildJoinArguments), so it already arrives here as an infinite stream
        // of the source frame, correctly spaced at the project's frame rate;
        // "n" below is simply that stream's own frame counter. That replaces
        // zoompan's self-contained per-input-frame duplication (its ":d="),
        // which crop has no equivalent for on its own.
        return $"crop=w='{widthExpression}':h='{heightExpression}':x='{xExpression}':y='{yExpression}'," +
               $"scale={options.Render.PreviewWidth}:{options.Render.PreviewHeight}";
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
