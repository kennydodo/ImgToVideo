using System.Globalization;

namespace ImgToVideo.Ffmpeg;

public sealed class Ffprobe
{
    private readonly string _ffprobePath;

    public Ffprobe(string ffprobePath = "ffprobe")
    {
        _ffprobePath = ToolLocator.Resolve("ffprobe", ffprobePath);
    }

    public async Task<double> GetDurationSecondsAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var runner = new FfmpegRunner(_ffprobePath);
        var result = await runner.RunAsync(
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            mediaPath,
        ], cancellationToken: cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"ffprobe failed for \"{mediaPath}\" (exit {result.ExitCode}): {result.ErrorTail}");
        }

        var line = result.StandardOutput
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (line is null || !double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new InvalidOperationException($"ffprobe returned no duration for \"{mediaPath}\".");
        }

        return seconds;
    }
}
