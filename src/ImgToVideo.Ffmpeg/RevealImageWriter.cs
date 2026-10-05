using System.Globalization;
using System.Text.RegularExpressions;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

/// <summary>
/// Writes the stills a reveal shot needs: step k of N is the source image with
/// only its first k of N equal-width slices visible, the rest black (same size
/// as the source). The planner only names the files (RevealPaths); this makes
/// them and returns the image list extended with them, so every exporter and
/// the renderer can treat them as ordinary images.
/// </summary>
public static class RevealImageWriter
{
    private static readonly Regex StepName = new(
        @"^(?<stem>.+)_reveal(?<k>\d+)of(?<n>\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        foreach (var (path, source, k, n) in missing)
        {
            var upToDate = File.Exists(path) &&
                           File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(source.FilePath);
            if (!upToDate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var process = await runner.RunAsync(
                    BuildArguments(source.FilePath, source.Width, source.Height, k, n, path),
                    cancellationToken: cancellationToken);
                if (!process.Success || !File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"Could not write the reveal still \"{Path.GetFileName(path)}\": {process.ErrorTail}");
                }
            }

            result.Add(new ImageInfo(path, source.Name, source.Width, source.Height));
        }

        return result;
    }

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

    private static List<(string Path, ImageInfo Source, int K, int N)> MissingSteps(
        Timeline timeline, IReadOnlyList<ImageInfo> images)
    {
        var known = new HashSet<string>(images.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
        var found = new List<(string, ImageInfo, int, int)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in timeline.Scenes.SelectMany(s => s.Clips))
        {
            if (known.Contains(clip.FilePath) || !seen.Add(clip.FilePath))
            {
                continue;
            }

            var match = StepName.Match(Path.GetFileNameWithoutExtension(clip.FilePath));
            if (!match.Success)
            {
                continue;
            }

            var k = int.Parse(match.Groups["k"].Value, CultureInfo.InvariantCulture);
            var n = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            var source = images.FirstOrDefault(i =>
                string.Equals(Path.GetFileNameWithoutExtension(i.FilePath), match.Groups["stem"].Value,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(RevealPaths.StepPath(i.FilePath, k, n), clip.FilePath,
                    StringComparison.OrdinalIgnoreCase));
            if (source is not null && k >= 1 && k < n)
            {
                found.Add((clip.FilePath, source, k, n));
            }
        }

        return found;
    }
}
