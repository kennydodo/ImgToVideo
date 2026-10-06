using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

/// <summary>
/// Finds the audio file behind a sound name. Order: the project's own sfx folder
/// (sfx\name.wav / .mp3 / .m4a / .ogg), then a small built-in pack that ffmpeg
/// generates on first use into out\sfx (pop variants, whoosh, ding, click, swipe, tick).
/// </summary>
public static class SoundEffectLibrary
{
    public const string ProjectFolderName = "sfx";

    private static readonly string[] Extensions = [".wav", ".mp3", ".m4a", ".ogg", ".flac"];

    /// <summary>Built-in sound name -> ffmpeg lavfi input and audio filter.</summary>
    private static readonly Dictionary<string, (string Source, string Filter)> BuiltIn =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The "pop" family. A shotlist's "pop" plays the project's Sound.DefaultPop (see ResolveAlias);
            // every variant is also usable by its own name. Peaks are levelled to about -3 dB.
            ["pop_classic"] = ("aevalsrc='0.8*sin(2*PI*(1100*t-3000*t*t))*exp(-16*t)':d=0.3:s=48000", "volume=-1.0dB"),
            ["pop_bubble"] = ("aevalsrc='0.8*sin(2*PI*(350*t+3500*t*t))*exp(-45*t)':d=0.15:s=48000", "afade=t=in:d=0.002,volume=-0.2dB"),
            ["pop_boop"] = ("aevalsrc='0.7*sin(2*PI*(480*t-400*t*t))*exp(-22*t)':d=0.25:s=48000", "afade=t=in:d=0.004,lowpass=f=2500,volume=1.0dB"),
            ["pop_tap"] = ("aevalsrc='0.8*sin(2*PI*700*t)*exp(-60*t)+0.35*sin(2*PI*1750*t)*exp(-90*t)+0.3*(random(0)*2-1)*exp(-400*t)':d=0.15:s=48000", "lowpass=f=5000,volume=-2.9dB"),
            ["pop_marimba"] = ("aevalsrc='0.6*sin(2*PI*784*t)*exp(-14*t)+0.25*sin(2*PI*3136*t)*exp(-40*t)':d=0.4:s=48000", "afade=t=in:d=0.002,volume=-0.8dB"),
            ["pop_cork"] = ("aevalsrc='0.8*sin(2*PI*(900*t-5500*t*t))*exp(-50*t)+0.3*(random(0)*2-1)*exp(-250*t)':d=0.15:s=48000", "lowpass=f=4500,afade=t=in:d=0.001,volume=-1.0dB"),
            ["pop_blip"] = ("aevalsrc='0.8*sin(2*PI*1000*t)*exp(-70*t)':d=0.1:s=48000", "afade=t=in:d=0.001,volume=-0.3dB"),
            ["pop_ping"] = ("aevalsrc='0.55*sin(2*PI*1760*t)*exp(-18*t)+0.25*sin(2*PI*2637*t)*exp(-25*t)':d=0.5:s=48000", "afade=t=in:d=0.002,volume=-0.3dB"),
            ["pop_drop"] = ("aevalsrc='0.8*sin(2*PI*(1400*t-2800*t*t))*exp(-30*t)':d=0.2:s=48000", "afade=t=in:d=0.002,lowpass=f=4000,volume=-0.4dB"),
            ["pop_thump"] = ("aevalsrc='0.9*sin(2*PI*(220*t-500*t*t))*exp(-18*t)':d=0.25:s=48000", "afade=t=in:d=0.003,volume=-1.5dB"),
            ["pop_snap"] = ("aevalsrc='(random(0)*2-1)*exp(-180*t)+0.4*sin(2*PI*1300*t)*exp(-120*t)':d=0.1:s=48000", "highpass=f=1800,lowpass=f=9000,volume=0.1dB"),
            ["pop_pluck"] = ("aevalsrc='0.55*sin(2*PI*440*t)*exp(-20*t)+0.28*sin(2*PI*880*t)*exp(-26*t)+0.18*sin(2*PI*1320*t)*exp(-32*t)':d=0.3:s=48000", "afade=t=in:d=0.002,volume=-0.6dB"),
            ["pop_sparkle"] = ("aevalsrc='0.6*sin(2*PI*1568*t)*exp(-40*t)+if(gt(t,0.06),0.6*sin(2*PI*2093*t)*exp(-40*(t-0.06)),0)':d=0.3:s=48000", "afade=t=in:d=0.001,volume=1.1dB"),
            ["ding"] = ("aevalsrc='0.5*sin(2*PI*1568*t)*exp(-5*t)+0.25*sin(2*PI*2349*t)*exp(-7*t)':d=1:s=48000", "anull"),
            ["click"] = ("aevalsrc='(random(0)*2-1)*exp(-120*t)':d=0.1:s=48000", "lowpass=f=6000,highpass=f=800,volume=1.5"),
            ["tick"] = ("aevalsrc='0.7*sin(2*PI*2200*t)*exp(-45*t)':d=0.12:s=48000", "anull"),
            ["whoosh"] = ("anoisesrc=d=0.7:c=pink:r=48000:a=1.0",
                "highpass=f=300,lowpass=f=5000,afade=t=in:d=0.3,afade=t=out:st=0.3:d=0.4,volume=8dB"),
            ["swipe"] = ("anoisesrc=d=0.45:c=white:r=48000:a=1.0",
                "highpass=f=1500,lowpass=f=7000,afade=t=in:d=0.12,afade=t=out:st=0.15:d=0.3,volume=-2dB"),
        };

    public static IReadOnlyCollection<string> BuiltInNames =>
        BuiltIn.Keys.Where(k => !k.StartsWith("pop_", StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { SoundCatalog.PopAlias }).Concat(BuiltIn.Keys.Where(k => k.StartsWith("pop_", StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>"pop" means the project's chosen pop variant; every other name is used as written.</summary>
    public static string ResolveAlias(string name, string? defaultPop)
    {
        if (!string.Equals(name, SoundCatalog.PopAlias, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return SoundCatalog.IsPopVariant(defaultPop) ? defaultPop! : SoundCatalog.DefaultPopId;
    }

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

    public static bool IsBuiltIn(string name) =>
        string.Equals(name, SoundCatalog.PopAlias, StringComparison.OrdinalIgnoreCase) || BuiltIn.ContainsKey(name);

    /// <summary>Writes (or reuses) the built-in sound; null when the name is not built in or ffmpeg fails.</summary>
    public static async Task<string?> EnsureBuiltInAsync(
        string projectDir, string name, FfmpegRunner runner, string? defaultPop = null,
        CancellationToken cancellationToken = default)
    {
        name = ResolveAlias(name, defaultPop);
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

    public static IReadOnlyList<string>? BuildArguments(string name, string outputPath, string? defaultPop = null)
    {
        if (!BuiltIn.TryGetValue(ResolveAlias(name, defaultPop), out var recipe))
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
