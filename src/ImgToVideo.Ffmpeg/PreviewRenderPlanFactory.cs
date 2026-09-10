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

        var cuts = ComputeTransitionCuts(clips);
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

            var cutIn = i > 0 ? cuts[i - 1] : 0;
            var cutOut = i < cuts.Length ? cuts[i] : 0;
            var pieceStart = cutIn > 0 ? (cutIn + 1) / 2 : 0;
            var pieceEnd = clip.DurationFrames - (cutOut > 0 ? cutOut / 2 : 0);
            var pieceCount = pieceEnd - pieceStart;

            var segmentPath = Path.Combine(outputDirectory, $"seg_{i + 1:D4}.mp4");
            segments.Add(new SegmentCommand(
                segments.Count + 1,
                segmentPath,
                BuildSegmentArguments(clip, size.Width, size.Height, pieceStart, pieceCount, options, segmentPath),
                pieceCount));
            totalFrames += pieceCount;

            if (cutOut > 0)
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
                        cutOut, options, joinPath),
                    cutOut));
                totalFrames += cutOut;
            }
        }

        var roughPath = Path.Combine(outputDirectory, "rough.mp4");
        return new RenderPlan(
            Segments: segments,
            ConcatListContent: BuildConcatList(segments),
            ConcatArguments: BuildConcatArguments(Path.Combine(outputDirectory, "concat.txt"), roughPath),
            RoughPath: roughPath,
            MuxArguments: BuildMuxArguments(roughPath, timeline.Audio.FilePath, previewPath),
            PreviewPath: previewPath,
            TotalFrames: totalFrames);
    }

    private static long[] ComputeTransitionCuts(IReadOnlyList<VideoClip> clips)
    {
        var cuts = new long[Math.Max(0, clips.Count - 1)];

        for (var c = 0; c < cuts.Length; c++)
        {
            var transition = clips[c + 1].Transition;
            if (transition is not { Kind: TransitionKind.Crossfade, DurationFrames: >= 2 })
            {
                continue;
            }

            var tail = transition.DurationFrames / 2;
            var head = (transition.DurationFrames + 1) / 2;
            if (clips[c].DurationFrames - tail < 1 || clips[c + 1].DurationFrames - head < 1)
            {
                continue;
            }

            cuts[c] = transition.DurationFrames;
        }

        for (var i = 0; i < clips.Count; i++)
        {
            var trim = (i > 0 && cuts[i - 1] > 0 ? (cuts[i - 1] + 1) / 2 : 0) +
                       (i < cuts.Length && cuts[i] > 0 ? cuts[i] / 2 : 0);
            if (clips[i].DurationFrames - trim < 1)
            {
                if (i > 0)
                {
                    cuts[i - 1] = 0;
                }

                if (i < cuts.Length)
                {
                    cuts[i] = 0;
                }
            }
        }

        return cuts;
    }

    private static IReadOnlyList<string> BuildSegmentArguments(
        VideoClip clip, int sourceWidth, int sourceHeight,
        long pieceStart, long pieceCount, ProjectOptions options, string outputPath)
    {
        if (pieceCount < 1)
        {
            throw new InvalidOperationException(
                $"Clip \"{clip.FilePath}\" has an empty render range; cannot render.");
        }

        var render = options.Render;
        var supersample = sourceWidth < SupersampleThreshold ? SupersampleFactor : 1;
        var scaledWidth = sourceWidth * supersample;
        var scaledHeight = sourceHeight * supersample;

        var chain = new List<string>(3);
        if (supersample > 1)
        {
            chain.Add($"scale={scaledWidth}:{scaledHeight}:flags=lanczos");
        }

        chain.Add(BuildZoompanFilter(
            clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options));
        chain.Add("format=yuv420p");

        return WrapSegmentArguments(
            new[] { "-i", clip.FilePath },
            null,
            string.Join(",", chain),
            pieceCount, render, outputPath);
    }

    private static IReadOnlyList<string> BuildJoinArguments(
        VideoClip outgoing, int outgoingWidth, int outgoingHeight,
        VideoClip incoming, int incomingWidth, int incomingHeight,
        long transitionFrames, ProjectOptions options, string outputPath)
    {
        var render = options.Render;
        var outTail = transitionFrames / 2;

        var outgoingChain = BuildJoinSideChain(
            outgoing, outgoingWidth, outgoingHeight,
            pieceStart: outgoing.DurationFrames - outTail, pieceCount: transitionFrames, options);
        var incomingChain = BuildJoinSideChain(
            incoming, incomingWidth, incomingHeight,
            pieceStart: -outTail, pieceCount: transitionFrames, options);

        var filterComplex =
            $"[0:v]{outgoingChain}[va];[1:v]{incomingChain}[vb];" +
            $"[va][vb]xfade=transition=fade:duration={F(transitionFrames / (double)options.Output.Fps)}:offset=0," +
            "format=yuv420p";

        return WrapSegmentArguments(
            new[] { "-i", outgoing.FilePath, "-i", incoming.FilePath },
            filterComplex,
            null,
            transitionFrames, render, outputPath);
    }

    private static string BuildJoinSideChain(
        VideoClip clip, int sourceWidth, int sourceHeight,
        long pieceStart, long pieceCount, ProjectOptions options)
    {
        var supersample = sourceWidth < SupersampleThreshold ? SupersampleFactor : 1;
        var scaledWidth = sourceWidth * supersample;
        var scaledHeight = sourceHeight * supersample;

        var chain = supersample > 1
            ? $"scale={scaledWidth}:{scaledHeight}:flags=lanczos," +
              BuildZoompanFilter(clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options)
            : BuildZoompanFilter(clip, sourceWidth, scaledWidth, scaledHeight, pieceStart, pieceCount, options);
        return chain;
    }

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

        var widthDelta = (clip.EndViewport.Width - clip.StartViewport.Width) / (double)steps;
        var xDelta = (clip.EndViewport.X - clip.StartViewport.X) / (double)steps;
        var yDelta = (clip.EndViewport.Y - clip.StartViewport.Y) / (double)steps;
        var heightDelta = (clip.EndViewport.Height - clip.StartViewport.Height) / (double)steps;

        double ViewportWidth(double f) => clip.StartViewport.Width + widthDelta * f;
        double ViewportX(double f) => clip.StartViewport.X + xDelta * f;
        double ViewportY(double f) => clip.StartViewport.Y + yDelta * f;

        double CenterX(double f) => supersample * (ViewportX(f) + ViewportWidth(f) / 2.0);
        double CenterY(double f) => supersample * (ViewportY(f) + ViewportHeight(f) / 2.0);
        double ViewportHeight(double f) => clip.StartViewport.Height + heightDelta * f;

        double Zoom(double f) => sourceWidth / ViewportWidth(f);

        var firstFrame = (double)pieceStart;
        var lastFrame = pieceStart + pieceCount - 1;
        var zoomStart = Zoom(firstFrame);
        var zoomEnd = Zoom(lastFrame);
        var zoomHigh = Math.Max(zoomStart, zoomEnd);
        var zoomLow = Math.Min(zoomStart, zoomEnd);

        string zoomExpression;
        string xExpression;
        string yExpression;
        if (pieceCount <= 1)
        {
            zoomExpression = F(zoomStart);
            xExpression = F(CenterX(firstFrame) - scaledWidth / zoomStart / 2.0);
            yExpression = F(CenterY(firstFrame) - scaledHeight / zoomStart / 2.0);
        }
        else
        {
            var offset = FormatOffset(pieceStart);
            zoomExpression =
                $"min({F(zoomHigh)},max({F(zoomLow)},{F(scaledWidth)}/({F(supersample * clip.StartViewport.Width)}+" +
                $"({F(supersample * widthDelta)})*{offset})))";
            xExpression =
                $"min(max(0,{F(CenterX(firstFrame))}+({F(CenterX(firstFrame + 1) - CenterX(firstFrame))})*{offset}-iw/zoom/2),iw-iw/zoom)";
            yExpression =
                $"min(max(0,{F(CenterY(firstFrame))}+({F(CenterY(firstFrame + 1) - CenterY(firstFrame))})*{offset}-ih/zoom/2),ih-ih/zoom)";
        }

        return $"zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}'" +
               $":d={pieceCount.ToString(CultureInfo.InvariantCulture)}" +
               $":s={options.Render.PreviewWidth}x{options.Render.PreviewHeight}" +
               $":fps={F(options.Output.Fps)}";
    }

    private static string FormatOffset(long pieceStart) =>
        pieceStart switch
        {
            0 => "(on+0)",
            > 0 => $"(on+{pieceStart.ToString(CultureInfo.InvariantCulture)})",
            _ => $"(on{pieceStart.ToString(CultureInfo.InvariantCulture)})",
        };

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
