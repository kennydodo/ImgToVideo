using System.Collections.Concurrent;

namespace ImgToVideo.Ffmpeg;

/// <summary>Probes the ffmpeg binary's available encoders, cached per binary path.</summary>
public static class EncoderProbe
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlySet<string>>> Cache = new();

    public static IReadOnlySet<string> GetEncoders(string ffmpegPath)
    {
        return Cache.GetOrAdd(ffmpegPath, path => new Lazy<IReadOnlySet<string>>(() => Load(path))).Value;
    }

    private static IReadOnlySet<string> Load(string ffmpegPath)
    {
        try
        {
            var result = new FfmpegRunner(ffmpegPath)
                .RunAsync(["-hide_banner", "-encoders"])
                .GetAwaiter()
                .GetResult();
            if (!result.Success)
            {
                return Empty;
            }

            var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in result.StandardOutput.Split('\n'))
            {
                // Encoder lines look like: " V....D h264_nvenc  NVIDIA NVENC H.264 encoder".
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Contains('V'))
                {
                    encoders.Add(parts[1]);
                }
            }

            return encoders;
        }
        catch (InvalidOperationException)
        {
            return Empty;
        }
        catch (AggregateException)
        {
            return Empty;
        }
    }

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
}
