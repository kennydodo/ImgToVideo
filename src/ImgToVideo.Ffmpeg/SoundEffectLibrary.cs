using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

/// <summary>
/// Finds the audio file behind a sound name. Order: the project's own sfx folder
/// (sfx\name.wav / .mp3 / .m4a / .ogg), then a small built-in pack that ffmpeg
/// generates on first use into out\sfx (pop, whoosh, ding, click, swipe, tick).
/// </summary>
public static class SoundEffectLibrary
{
    public const string ProjectFolderName = "sfx";

    private static readonly string[] Extensions = [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    /// <summary>Built-in sound name -> ffmpeg lavfi input and audio filter.</summary>
    private static readonly Dictionary<string, (string Source, string Filter)> BuiltIn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pop"] = ("aevalsrc='0.8*sin(2*PI*(1100*t-3000*t*t))*exp(-16*t)':d=0.3:s=48000", "anull"),
            ["ding"] = ("aevalsrc='0.5*sin(2*PI*1568*t)*exp(-5*t)+0.25*sin(2*PI*2349*t)*exp(-7*t)':d=1:s=48000", "anull"),
            ["click"] = ("aevalsrc='(random(0)*2-1)*exp(-120*t)':d=0.1:s=48000", "lowpass=f=6000,highpass=f=800,volume=1.5"),
            ["tick"] = ("aevalsrc='0.7*sin(2*PI*2200*t)*exp(-45*t)':d=0.12:s=48000", "anull"),
            ["whoosh"] = ("anoisesrc=d=0.7:c=pink:r=48000:a=1.0",
                "highpass=f=300,lowpass=f=5000,afade=t=in:d=0.3,afade=t=out:st=0.3:d=0.4,volume=8dB"),
            ["swipe"] = ("anoisesrc=d=0.45:c=white:r=48000:a=1.0",
                "highpass=f=1500,lowpass=f=7000,afade=t=in:d=0.12,afade=t=out:st=0.15:d=0.3,volume=-2dB"),
        };

    public static IReadOnlyCollection<string> BuiltInNames => BuiltIn.Keys;

    /// <summary>The user's own file for <paramref name="name"/>, or null.</summary>
    public static string? FindProjectFile(string projectDir, string name)
    {
        var folder = Path.Combine(projectDir, ProjectFolderName);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        // A name with an extension is used as given.
        var direct = Path.Combine(folder, name);
        if (Path.HasExtension(name) && File.Exists(direct))
        {
            return direct;
        }

        foreach (var extension in Extensions)
        {
            var candidate = Path.Combine(folder, name + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool IsBuiltIn(string name) => BuiltIn.ContainsKey(name);

    /// <summary>Writes (or reuses) the built-in sound; null when the name is not built in or ffmpeg fails.</summary>
    public static async Task<string?> EnsureBuiltInAsync(
        string projectDir, string name, FfmpegRunner runner, CancellationToken cancellationToken = default)
    {
        if (!BuiltIn.TryGetValue(name, out var recipe))
        {
            return null;
        }

        var path = Path.Combine(projectDir, "out", "sfx", name.ToLowerInvariant() + ".wav");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = await runner.RunAsync(BuildArguments(name, path)!, cancellationToken: cancellationToken);
        return result.Success && File.Exists(path) ? path : null;
    }

    public static IReadOnlyList<string>? BuildArguments(string name, string outputPath)
    {
        if (!BuiltIn.TryGetValue(name, out var recipe))
        {
            return null;
        }

        return new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", recipe.Source,
            "-af", recipe.Filter,
            "-ac", "1", "-ar", "48000", "-c:a", "pcm_s16le",
            outputPath,
        };
    }
}
