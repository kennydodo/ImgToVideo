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
            MuxArguments: BuildMuxArguments(
                roughPath, timeline.Audio.FilePath, previewPath, timeline.Sounds, timeline.Fps),
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
    /// particular - see BuildFixedSizeCropFilter) see a plain sequential frame
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
        var sizeChanges = ViewportSizeChanges(clip);

        if (viewportsInsideImage)
        {
            var supersample = SupersampleFor(sourceWidth, (int)SupersampleTargetFor(options));
            var scaledWidth = sourceWidth * supersample;
            var scaledHeight = sourceHeight * supersample;
            var chain = supersample > 1
                ? $"scale={scaledWidth}:{scaledHeight}:flags=lanczos,"
                : string.Empty;

            // See ViewportSizeChanges: zoompan pulls exactly one upstream frame
            // per segment on its own (its "d" is a self-contained repeat count,
            // not a real per-frame stream request), so - unlike the crop path -
            // it needs no LoopStage to keep the supersample scale from being
            // recomputed per output frame.
            return sizeChanges
                ? chain + BuildZoomPanFilter(
                    clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options) +
                    ",format=yuv420p"
                : chain + LoopStage(pieceCount) + BuildFixedSizeCropFilter(
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
        var padChain = $"scale={scaledImageWidth}:{scaledImageHeight}:flags=lanczos," +
               $"pad={scaledCanvasWidth}:{scaledCanvasHeight}:(ow-iw)/2:(oh-ih)/2,";

        return sizeChanges
            ? padChain + BuildZoomPanFilter(
                clip, canvasWidth, scaledCanvasWidth, scaledCanvasHeight, pieceStart, pieceCount, options) +
                ",format=yuv420p"
            : padChain + LoopStage(pieceCount) + BuildFixedSizeCropFilter(
                clip, canvasWidth, scaledCanvasWidth, scaledCanvasHeight, pieceStart, pieceCount, options) +
                ",format=yuv420p";
    }

    /// <summary>
    /// True when a clip's viewport width or height actually differs between
    /// <see cref="VideoClip.StartViewport"/> and <see cref="VideoClip.EndViewport"/>
    /// (Zoom In/Out, Push In) as opposed to staying fixed while only position
    /// moves (Pan Left/Right/Up/Down, Static). This decides which filter
    /// <see cref="BuildSideChain"/> uses - see <see cref="BuildFixedSizeCropFilter"/>
    /// and <see cref="BuildZoomPanFilter"/> for why the two cases cannot share
    /// one implementation.
    /// </summary>
    private static bool ViewportSizeChanges(VideoClip clip) =>
        Math.Abs(clip.StartViewport.Width - clip.EndViewport.Width) > 0.01 ||
        Math.Abs(clip.StartViewport.Height - clip.EndViewport.Height) > 0.01;

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
        // render.Encoder is "auto" by default - ResolveEncoder is what actually
        // probes ffmpeg for an available hardware encoder (NVENC/AMF/QSV) and
        // falls back to libx264. Passing render.Encoder straight through here
        // used to skip that probe entirely: VideoEncoderArgs' switch doesn't
        // recognize "auto" and silently fell to its libx264 default, so every
        // render ran on the CPU even on a machine with a capable GPU. An
        // already-explicit encoder (e.g. a user who typed "h264_nvenc" into
        // Settings) is passed straight through as before - only "auto" goes
        // through the probe.
        var resolvedEncoder =
            string.IsNullOrWhiteSpace(render.Encoder) ||
            render.Encoder.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? ResolveEncoder(render)
                : render.Encoder;
        args.AddRange(VideoEncoderArgs(resolvedEncoder, render.PreviewPreset,
                                       render.PreviewCrf, render.PreviewBframes));
        args.Add("-an");
        args.Add(outputPath);
        return args;
    }

    /// <summary>
    /// Resolves the configured encoder to a concrete ffmpeg encoder name, falling
    /// back to CPU libx264 when a hardware encoder is requested but unavailable.
    /// Checks against <see cref="ResolveEffectiveFfmpegPath"/>, not the raw
    /// configured path, so this always matches whichever ffmpeg binary the
    /// render will actually run (auto mode may pick a different one than
    /// render.FfmpegPath names - see that method).
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

        var ffmpegPath = ResolveEffectiveFfmpegPath(render);
        foreach (var candidate in candidates)
        {
            // EncoderProbe.Supports does more than check that ffmpeg was built
            // with this encoder - it verifies the encoder actually initializes
            // on this machine right now, which a compiled-in hardware encoder
            // can still fail to do (e.g. an Nvidia driver too old for the
            // nvenc API version this ffmpeg build expects). Selecting on that
            // instead of mere presence keeps a render from picking an encoder
            // that is only going to fail once real segments start encoding.
            if (EncoderProbe.Supports(ffmpegPath, candidate))
            {
                return candidate;
            }
        }

        return "libx264";
    }

    /// <summary>
    /// The ffmpeg binary a render should actually run, which is not always
    /// simply <see cref="RenderOptions.FfmpegPath"/> resolved through
    /// <see cref="ToolLocator.Resolve"/>.
    ///
    /// Hardware encoding should not depend on a manually-set ffmpeg_path
    /// surviving in the project's config: that field is easy to lose (e.g. it
    /// gets silently reset if Settings is opened and saved before the project
    /// has been analyzed at least once, since the Settings dialog only knows
    /// about whatever ProjectOptions the app already has in memory) and a
    /// user has no reason to expect editing an unrelated setting to quietly
    /// turn off GPU rendering. So in "auto" encoder mode, if the configured
    /// ffmpeg can't actually deliver a working hardware encoder - e.g. its
    /// NVENC build expects a newer driver API version than the installed
    /// Nvidia driver provides - every other ffmpeg.exe this machine's normal
    /// search locations turn up (see <see cref="ToolLocator.ResolveAllCandidates"/>,
    /// which includes common portable-tool locations such as an alternate
    /// build kept under C:\Tools) is checked in turn, and the first one that
    /// does support a hardware encoder is used instead - automatically, every
    /// render, independent of whatever render.FfmpegPath happens to say.
    ///
    /// An explicit encoder choice (a user who deliberately typed
    /// "h264_nvenc", or "libx264"/"cpu" to force CPU) is never second-guessed
    /// this way: only "auto" (or blank) triggers the search, and the
    /// configured ffmpeg is always used for that.
    /// </summary>
    public static string ResolveEffectiveFfmpegPath(RenderOptions render)
    {
        var configuredPath = ToolLocator.Resolve("ffmpeg", render.FfmpegPath);

        var encoder = (render.Encoder ?? "auto").Trim();
        var isAuto = encoder.Length == 0 || encoder.Equals("auto", StringComparison.OrdinalIgnoreCase);
        if (!isAuto || HasWorkingHardwareEncoder(configuredPath))
        {
            return configuredPath;
        }

        foreach (var candidate in ToolLocator.ResolveAllCandidates("ffmpeg", render.FfmpegPath))
        {
            if (!string.Equals(candidate, configuredPath, StringComparison.OrdinalIgnoreCase) &&
                HasWorkingHardwareEncoder(candidate))
            {
                return candidate;
            }
        }

        return configuredPath;
    }

    private static bool HasWorkingHardwareEncoder(string ffmpegPath) =>
        EncoderProbe.Supports(ffmpegPath, "h264_nvenc") ||
        EncoderProbe.Supports(ffmpegPath, "h264_amf") ||
        EncoderProbe.Supports(ffmpegPath, "h264_qsv");

    /// <summary>
    /// One-line summary of what a render with these options will actually use -
    /// "h264_nvenc via C:\path\to\ffmpeg.exe" or similar. Whether the resolved
    /// encoder ends up CPU (libx264) is normally invisible until someone
    /// happens to check Task Manager mid-render; surfacing it directly in the
    /// UI after each build removes the guesswork around whether "still uses
    /// CPU" means the encoder pick failed, or simply that the render's CPU
    /// cost is dominated by something encoding can't speed up (heavy filter
    /// work - e.g. a large supersample_target_width - runs on the CPU
    /// regardless of which encoder finishes the segment).
    /// </summary>
    public static string DescribeEffectiveEncoder(RenderOptions render)
    {
        var encoder =
            string.IsNullOrWhiteSpace(render.Encoder) ||
            render.Encoder.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? ResolveEncoder(render)
                : render.Encoder;
        return $"{encoder} via {ResolveEffectiveFfmpegPath(render)}";
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
    /// Caches the single frame produced by the (potentially expensive)
    /// supersample <c>scale</c>/<c>pad</c> stage that precedes this call and
    /// replays it <paramref name="pieceCount"/> times, so that expensive stage
    /// only actually runs once per segment instead of once per output frame.
    ///
    /// The source is opened with "-loop 1 -r fps" (see
    /// <see cref="SourceInputArguments"/>) purely so downstream filters see
    /// correctly-timed, sequentially-numbered frames - that fixed a timing bug
    /// in an earlier version of this pipeline. But it has a costly side effect:
    /// every one of those duplicated input frames is a distinct frame as far as
    /// the filter graph is concerned, so anything placed upstream of this
    /// filter (in particular the supersample <c>scale=...:flags=lanczos</c>,
    /// which can be very expensive on large sources) was being recomputed from
    /// scratch for every single output frame - e.g. 150-200+ times per segment
    /// - even though its result is identical every time. This filter caches
    /// that first computed frame (<c>size=1</c>) and serves the cached copy for
    /// the remaining <c>pieceCount - 1</c> repeats, so the expensive upstream
    /// work effectively happens exactly once per segment while crop/pan (which
    /// comes after this and does need a per-frame "n" counter) still sees
    /// pieceCount distinct, correctly-numbered frames.
    /// </summary>
    private static string LoopStage(long pieceCount) =>
        $"loop=loop={Math.Max(0, pieceCount - 1).ToString(CultureInfo.InvariantCulture)}:size=1:start=0,";

    /// <summary>
    /// Builds the pan stage of the side chain for clips whose viewport size
    /// never changes (Pan Left/Right/Up/Down, Static) as an explicit
    /// <c>crop</c> - a fixed, precomputed width/height, with only x and y
    /// interpolated between <see cref="VideoClip.StartViewport"/> and
    /// <see cref="VideoClip.EndViewport"/> - followed by a plain resize to the
    /// render's preview size.
    ///
    /// This (and <see cref="BuildZoomPanFilter"/> for clips whose size DOES
    /// change) replaced an earlier single <c>zoompan</c>-based implementation.
    /// zoompan's <c>z</c> (zoom) parameter always crops <c>iw/z</c> by
    /// <c>ih/z</c> - the SAME ratio on both axes as the source frame - so it
    /// can only ever produce a crop window with the source's own aspect
    /// ratio. That is fine for Zoom In/Out (which shrink evenly on both axes)
    /// and incidentally close enough for Pan Left/Right (which move only in
    /// X), but it cannot express Pan Up/Down at all: those only change Y, so
    /// the single shared zoom value stays fixed at 1 for the whole clip,
    /// which forces zoompan's y-clamp bound (<c>ih-ih/zoom</c>) to exactly 0
    /// and the pan never moves. The same coupling also over-crops Pan
    /// Left/Right vertically whenever the source isn't already exactly the
    /// output's aspect ratio. Since <see cref="VideoClip.StartViewport"/>/
    /// <see cref="VideoClip.EndViewport"/> are always constructed (see
    /// MotionEngine.PlanClip) to already match the output aspect ratio, an
    /// explicit crop needs no zoom coupling at all for a pan: x and y move
    /// independently, and the trailing scale is a clean, non-distorting
    /// resize because the crop's aspect always matches
    /// PreviewWidth:PreviewHeight already.
    ///
    /// Width and height are passed to <c>crop</c> as plain precomputed
    /// numbers rather than "n"-dependent expressions - deliberately, even
    /// though they're mathematically constant here anyway. ffmpeg's
    /// <c>crop</c> filter fixes its OUTPUT LINK's frame size once, when the
    /// filter is configured, because every filter downstream of it in the
    /// graph is negotiated against a single fixed frame size; consequently
    /// its <c>w</c>/<c>h</c> expressions are only ever evaluated ONCE, at
    /// that configuration point (effectively with "n" fixed at 0), never
    /// again for later frames, no matter what they reference. (x/y, which
    /// only shift the crop's position and never its output size, have no
    /// such restriction and genuinely re-evaluate every frame - confirmed by
    /// direct reproduction.) An earlier version of this method embedded "n"
    /// in width/height too, for every motion type including pans - for a
    /// pan, since width/height are already constant, that silently evaluated
    /// to the same value "n" would have given at frame 0 anyway, so nothing
    /// looked wrong. But it meant that for Zoom In/Out and Push In - which
    /// actually need width/height to change over the clip - the crop window
    /// was frozen at its very first frame's size for the ENTIRE remainder of
    /// the segment: verified directly by rendering a real clip and finding
    /// well over a hundred consecutive output frames byte-for-byte identical.
    /// Those motion types are now built by <see cref="BuildZoomPanFilter"/>
    /// instead, whose zoompan-based <c>z</c> is a sampling parameter rather
    /// than an output-size parameter and so is not subject to this limit.
    ///
    /// Because <c>crop</c> does not itself expand a single input frame into
    /// <paramref name="pieceCount"/> output frames, the source is opened with
    /// "-loop 1 -r fps" (see <see cref="SourceInputArguments"/>) so it
    /// already arrives here as a correctly-timed, indefinitely repeating
    /// stream; crop's own per-frame <c>n</c> variable then plays the same
    /// role zoompan's "on" does for <see cref="BuildZoomPanFilter"/>. That
    /// input-level duplication is also why any expensive filter upstream of
    /// this one (the supersample scale) needs the separate <see cref="LoopStage"/>
    /// cache-and-replay filter placed just before this call - otherwise it
    /// would be recomputed once per output frame instead of once per segment.
    /// </summary>
    private static string BuildFixedSizeCropFilter(
        VideoClip clip, long sourceWidth, long scaledWidth, long scaledHeight,
        long pieceStart, long pieceCount, ProjectOptions options)
    {
        var clipDuration = clip.DurationFrames;
        var steps = clipDuration > 1 ? clipDuration - 1 : 1;
        var motionSteps = clip.MotionDurationFrames is { } motionEnd && motionEnd > 1
            ? Math.Max(1, Math.Min(motionEnd - 1, steps))
            : steps;
        var supersample = (double)scaledWidth / sourceWidth;

        // Width/height are identical at both ends - that's what routes a clip
        // to this method instead of BuildZoomPanFilter - so there is exactly
        // one true value each, computed once, never an "n"-dependent
        // expression (see this method's doc comment for why that matters).
        var width = supersample * clip.StartViewport.Width;
        var height = supersample * clip.StartViewport.Height;
        var xStart = supersample * clip.StartViewport.X;
        var xEnd = supersample * clip.EndViewport.X;
        var yStart = supersample * clip.StartViewport.Y;
        var yEnd = supersample * clip.EndViewport.Y;

        string xExpression;
        string yExpression;
        if (pieceCount <= 1)
        {
            var progress = EasingValue(clip.Easing, clipDuration > 1 ? (double)pieceStart / motionSteps : 0.0);
            xExpression = F(xStart + (xEnd - xStart) * progress);
            yExpression = F(yStart + (yEnd - yStart) * progress);
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
            xExpression = $"min(iw-ow,max(0,{F(xStart)}+({F(xEnd - xStart)})*{progress}))";
            yExpression = $"min(ih-oh,max(0,{F(yStart)}+({F(yEnd - yStart)})*{progress}))";
        }

        return $"crop=w='{F(width)}':h='{F(height)}':x='{xExpression}':y='{yExpression}'," +
               $"scale={options.Render.PreviewWidth}:{options.Render.PreviewHeight}";
    }

    /// <summary>
    /// Builds the pan/zoom stage of the side chain for clips whose viewport
    /// size DOES change between <see cref="VideoClip.StartViewport"/> and
    /// <see cref="VideoClip.EndViewport"/> (Zoom In/Out, Push In), using
    /// <c>zoompan</c>. See <see cref="BuildFixedSizeCropFilter"/>'s doc
    /// comment for the full story: <c>crop</c> cannot animate its own
    /// width/height per frame (only x/y), because ffmpeg fixes a filter's
    /// OUTPUT frame size once at configuration time, so it was never a viable
    /// primitive for these two motion types despite briefly replacing
    /// zoompan for everything. zoompan's <c>z</c> is a sampling parameter,
    /// not an output-size parameter - its output stays fixed at <c>s=</c> - so
    /// it re-evaluates correctly every frame; its known limitation (a single
    /// zoom value crops both axes by the same ratio, which cannot express an
    /// independent-axis pan) simply doesn't apply here, since Zoom In/Out and
    /// Push In are already a symmetric shrink/grow on both axes by
    /// construction (see MotionEngine.PlanClip's ZoomIn/ZoomOut case).
    ///
    /// Unlike <see cref="BuildFixedSizeCropFilter"/>, this does not need
    /// <see cref="LoopStage"/> before it: zoompan's own <c>d</c> parameter is
    /// a self-contained "repeat this one input frame this many times before
    /// asking upstream for another" counter (its "on" variable counts through
    /// those repeats), so - exactly like the cache LoopStage builds for crop -
    /// it only ever pulls a single frame through the (potentially expensive)
    /// upstream supersample scale for the whole segment, with no extra filter
    /// required to make that happen.
    /// </summary>
    private static string BuildZoomPanFilter(
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
            var progress = EasingValue(clip.Easing, clipDuration > 1 ? (double)pieceStart / motionSteps : 0.0);
            var width = clip.StartViewport.Width + (clip.EndViewport.Width - clip.StartViewport.Width) * progress;
            var centerX = (clip.StartViewport.X + clip.StartViewport.Width / 2.0) +
                          ((clip.EndViewport.X - clip.StartViewport.X) * progress);
            var centerY = (clip.StartViewport.Y + clip.StartViewport.Height / 2.0) +
                          ((clip.EndViewport.Y - clip.StartViewport.Y) * progress);
            var zoom = sourceWidth / width;

            zoomExpression = F(zoom);
            xExpression = F(supersample * centerX - scaledWidth / zoom / 2.0);
            yExpression = F(supersample * centerY - scaledHeight / zoom / 2.0);
        }
        else
        {
            var rawProgress = $"((on{SignedOffset(pieceStart)})/{motionSteps})";
            if (motionSteps != steps)
            {
                // The move completes after motionSteps frames and the framing holds.
                rawProgress = $"min(1,{rawProgress})";
            }

            var progress = EasingExpression(clip.Easing, rawProgress);

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

    /// <summary>Gain applied to every sound effect so it sits under the narration.</summary>
    private const double SoundGain = 0.8;

    public static IReadOnlyList<string> BuildMuxArguments(
        string roughPath, string audioPath, string previewPath,
        IReadOnlyList<SoundEffect>? sounds = null, double fps = 30)
    {
        var usable = (sounds ?? [])
            .Where(sound => !string.IsNullOrWhiteSpace(sound.FilePath))
            .OrderBy(sound => sound.StartFrame)
            .ToList();
        var args = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", roughPath,
            "-i", audioPath,
        };
        if (usable.Count == 0)
        {
            args.AddRange(new[]
            {
                "-map", "0:v:0",
                "-map", "1:a:0",
                "-c:v", "copy",
                "-c:a", "aac",
                "-b:a", "192k",
                "-shortest",
                previewPath,
            });
            return args;
        }

        // Narration plus each sound delayed to its frame, mixed at full level (no auto-normalising).
        const string common = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";
        var graph = new List<string> { $"[1:a]{common}[n]" };
        var labels = new StringBuilder("[n]");
        for (var i = 0; i < usable.Count; i++)
        {
            args.Add("-i");
            args.Add(usable[i].FilePath);
            var delayMs = (long)Math.Round(usable[i].StartFrame * 1000.0 / fps);
            graph.Add($"[{i + 2}:a]{common},adelay={delayMs}:all=1,volume={F(SoundGain)}[s{i}]");
            labels.Append($"[s{i}]");
        }

        graph.Add($"{labels}amix=inputs={usable.Count + 1}:duration=first:dropout_transition=0:normalize=0[aout]");
        args.AddRange(new[]
        {
            "-filter_complex", string.Join(";", graph),
            "-map", "0:v:0",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            previewPath,
        });
        return args;
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
