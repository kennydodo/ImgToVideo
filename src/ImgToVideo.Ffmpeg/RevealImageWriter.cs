using System.Globalization;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

/// <summary>
/// Writes the stills a reveal shot needs, driven by the recipe on each reveal clip:
/// - row: the source image with only its first k of N equal-width slices visible, rest black;
/// - grid: the source image with only its first k of 4 quadrants visible (TL, TR, BL, BR), rest black;
/// - separate images: a black canvas with images 1..k each cover-cropped into its slot.
/// The planner only names the files (RevealPaths); this makes them and returns the image list
/// extended with them, so every exporter and the renderer can treat them as ordinary images.
/// </summary>
public static class RevealImageWriter
{
    public static bool NeedsImages(Timeline timeline, IReadOnlyList<ImageInfo> images) =>
        MissingSteps(timeline, images).Count > 0;

    public static async Task<IReadOnlyList<ImageInfo>> EnsureAsync(
        Timeline timeline, IReadOnlyList<ImageInfo> images, FfmpegRunner runner,
        CancellationToken cancellationToken = default)
    {
        var missing = MissingSteps(timeline, images);
        if (missing.Count == 0)
        {
            return images;
        }

        var result = new List<ImageInfo>(images);
        var width = timeline.Resolution.Width;
        var height = timeline.Resolution.Height;
        foreach (var step in missing)
        {
            var sources = step.Info.IsStack
                ? step.Info.Sources.Take(step.Info.Visible).ToList()
                : new List<string> { step.Info.SourcePath! };
            var first = images.FirstOrDefault(i => SamePath(i.FilePath, sources[0]));
            if (first is null)
            {
                continue;
            }

            var (w, h) = step.Info.IsStack ? (width, height) : (first.Width, first.Height);
            var upToDate = File.Exists(step.Path) &&
                           sources.All(s => File.GetLastWriteTimeUtc(step.Path) >= File.GetLastWriteTimeUtc(s));
            if (!upToDate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(step.Path)!);
                var arguments = step.Info.IsStack
                    ? BuildStackArguments(sources, w, h, step.Info.Count, step.Info.Layout, step.Path)
                    : step.Info.Layout == RevealLayouts.Grid
                        ? BuildGridArguments(sources[0], w, h, step.Info.Visible, step.Path)
                        : BuildArguments(sources[0], w, h, step.Info.Visible, step.Info.Count, step.Path);
                var process = await runner.RunAsync(arguments, cancellationToken: cancellationToken);
                if (!process.Success || !File.Exists(step.Path))
                {
                    throw new InvalidOperationException(
                        $"Could not write the reveal still \"{Path.GetFileName(step.Path)}\": {process.ErrorTail}");
                }
            }

            result.Add(new ImageInfo(step.Path, first.Name, w, h));
        }

        return result;
    }

    /// <summary>Row reveal: the first k of n equal-width slices, the rest black.</summary>
    public static IReadOnlyList<string> BuildArguments(
        string sourcePath, int width, int height, int k, int n, string outputPath)
    {
        var visible = Math.Max(1, width * k / n);
        var f = CultureInfo.InvariantCulture;
        return new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", sourcePath,
            "-vf", string.Create(f, $"crop={visible}:{height}:0:0,pad={width}:{height}:0:0:color=black"),
            "-frames:v", "1",
            outputPath,
        };
    }

    /// <summary>Grid reveal: the first k of 4 quadrants (TL, TR, BL, BR) visible; later ones are black.</summary>
    public static IReadOnlyList<string> BuildGridArguments(
        string sourcePath, int width, int height, int k, string outputPath)
    {
        var halfW = width / 2;
        var halfH = height / 2;
        var corners = new (int X, int Y)[] { (0, 0), (width - halfW, 0), (0, height - halfH), (width - halfW, height - halfH) };
        var boxes = new List<string>();
        for (var q = Math.Max(0, k); q < 4; q++)
        {
            boxes.Add(string.Create(CultureInfo.InvariantCulture,
                $"drawbox=x={corners[q].X}:y={corners[q].Y}:w={halfW}:h={halfH}:color=black:t=fill"));
        }

        return new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-y",
            "-i", sourcePath,
            "-vf", boxes.Count > 0 ? string.Join(",", boxes) : "null",
            "-frames:v", "1",
            outputPath,
        };
    }

    /// <summary>Separate images: each cover-cropped into its slot of a black canvas.</summary>
    public static IReadOnlyList<string> BuildStackArguments(
        IReadOnlyList<string> sources, int width, int height, int count, string layout, string outputPath)
    {
        var f = CultureInfo.InvariantCulture;
        var slots = SlotRects(width, height, count, layout);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", string.Create(f, $"color=c=black:s={width}x{height}:d=1:r=1"),
        };
        foreach (var source in sources)
        {
            args.Add("-i");
            args.Add(source);
        }

        var graph = new List<string>();
        var previous = "0:v";
        for (var i = 0; i < sources.Count; i++)
        {
            var (x, y, w, h) = slots[i];
            graph.Add(string.Create(f,
                $"[{i + 1}:v]scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h},setsar=1[s{i}]"));
            var label = $"o{i}";
            graph.Add(string.Create(f, $"[{previous}][s{i}]overlay={x}:{y}[{label}]"));
            previous = label;
        }

        args.Add("-filter_complex");
        args.Add(string.Join(";", graph));
        args.Add("-map");
        args.Add($"[{previous}]");
        args.Add("-frames:v");
        args.Add("1");
        args.Add(outputPath);
        return args;
    }

    /// <summary>The slot (x, y, w, h) of each of <paramref name="count"/> items on the canvas.</summary>
    public static IReadOnlyList<(int X, int Y, int W, int H)> SlotRects(int width, int height, int count, string layout)
    {
        var slots = new List<(int, int, int, int)>();
        if (RevealLayouts.Normalize(layout) == RevealLayouts.Grid)
        {
            var w = width / 2;
            var h = height / 2;
            for (var i = 0; i < count; i++)
            {
                slots.Add((i % 2 == 0 ? 0 : width - w, i / 2 == 0 ? 0 : height - h, w, h));
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var x0 = width * i / count;
                var x1 = width * (i + 1) / count;
                slots.Add((x0, 0, x1 - x0, height));
            }
        }

        return slots;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static List<(string Path, RevealInfo Info)> MissingSteps(
        Timeline timeline, IReadOnlyList<ImageInfo> images)
    {
        var known = new HashSet<string>(images.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        var found = new List<(string, RevealInfo)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in timeline.Scenes.SelectMany(s => s.Clips))
        {
            if (clip.Reveal is null || known.Contains(clip.FilePath) || !seen.Add(clip.FilePath))
            {
                continue;
            }

            if (!clip.Reveal.IsStack && (clip.Reveal.SourcePath is null || clip.Reveal.Visible >= clip.Reveal.Count))
            {
                continue;
            }

            found.Add((clip.FilePath, clip.Reveal));
        }

        return found;
    }
}
