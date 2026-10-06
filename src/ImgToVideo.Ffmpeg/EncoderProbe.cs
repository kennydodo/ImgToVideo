using System.Collections.Concurrent;

namespace ImgToVideo.Ffmpeg;

/// <summary>Probes the ffmpeg binary's available encoders, cached per binary path.</summary>
public static class EncoderProbe
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlySet<string>>> Cache = new();
    private static readonly ConcurrentDictionary<(string FfmpegPath, string Encoder), Lazy<bool>> VerifyCache = new();

    public static IReadOnlySet<string> GetEncoders(string ffmpegPath)
    {
        return Cache.GetOrAdd(ffmpegPath, path => new Lazy<IReadOnlySet<string>>(() => Load(path))).Value;
    }

    /// <summary>
    /// True if <paramref name="encoder"/> is not just compiled into this ffmpeg
    /// build but can actually initialize on this machine right now.
    /// "ffmpeg -encoders" (see <see cref="GetEncoders"/>) only reports what the
    /// binary was built with - a hardware encoder listed there can still fail
    /// to open at runtime, e.g. NVENC refusing to initialize with "Driver does
    /// not support the required nvenc API version" when the installed Nvidia
    /// driver is older than the one ffmpeg was built against. Encoding one
    /// throwaway frame catches that up front, during encoder selection,
    /// instead of letting it fail an actual render segment partway through.
    /// Cached per (ffmpegPath, encoder) so the extra process launch only
    /// happens once per candidate per run.
    /// </summary>
    public static bool Supports(string ffmpegPath, string encoder)
    {
        if (!GetEncoders(ffmpegPath).Contains(encoder))
        {
            return false;
        }

        return VerifyCache.GetOrAdd(
            (ffmpegPath, encoder),
            key => new Lazy<bool>(() => Verify(key.FfmpegPath, key.Encoder))).Value;
    }

    private static bool Verify(string ffmpegPath, string encoder)
    {
        try
        {
            // Probe with the exact arguments a render would use (preset mapping, B-frames),
            // not just "-c:v <encoder>": an encoder that opens with defaults but rejects the
            // render's real options would otherwise pass here and fail every segment.
            var arguments = new List<string>
            {
                "-hide_banner",
                "-f", "lavfi",
                "-i", "color=black:s=64x64:d=0.1",
                "-frames:v", "1",
            };
            arguments.AddRange(PreviewRenderPlanFactory.VideoEncoderArgs(encoder, "ultrafast", 28, 0));
            arguments.AddRange(["-f", "null", "-"]);
            var result = new FfmpegRunner(ffmpegPath)
                .RunAsync(arguments)
                .GetAwaiter()
                .GetResult();
            return result.Success;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (AggregateException)
        {
            return false;
        }
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
