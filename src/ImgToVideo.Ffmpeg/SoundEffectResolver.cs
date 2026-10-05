using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

/// <summary>
/// Looks up every sound on the timeline (project sfx folder first, then the built-in pack),
/// fills in its file and length, and drops the ones that cannot be found, reporting them.
/// </summary>
public static class SoundEffectResolver
{
    /// <summary>Longest a sound may run; longer files are kept but warned about.</summary>
    public const double LongSoundSeconds = 3.0;

    public static async Task<IReadOnlyList<ValidationIssue>> ResolveAsync(
        Timeline timeline, string projectDir, FfmpegRunner runner, Ffprobe probe,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        if (timeline.Sounds.Count == 0)
        {
            return issues;
        }

        var files = new Dictionary<string, (string Path, long Frames)?>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<SoundEffect>();
        foreach (var sound in timeline.Sounds)
        {
            if (!files.TryGetValue(sound.Name, out var found))
            {
                found = await ResolveOneAsync(sound.Name, timeline.Fps, projectDir, runner, probe, issues, cancellationToken);
                files[sound.Name] = found;
            }

            if (found is null)
            {
                continue;
            }

            sound.FilePath = found.Value.Path;
            sound.DurationFrames = found.Value.Frames;
            kept.Add(sound);
        }

        timeline.Sounds.Clear();
        timeline.Sounds.AddRange(kept);
        return issues;
    }

    private static async Task<(string Path, long Frames)?> ResolveOneAsync(
        string name, double fps, string projectDir, FfmpegRunner runner, Ffprobe probe,
        List<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        var path = SoundEffectLibrary.FindProjectFile(projectDir, name)
                   ?? await SoundEffectLibrary.EnsureBuiltInAsync(projectDir, name, runner, cancellationToken);
        if (path is null)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "SFX_UNKNOWN",
                $"Sound \"{name}\" was not found: put {name}.wav (or .mp3) in the project's sfx folder, or use one of " +
                $"the built-in sounds ({string.Join(", ", SoundEffectLibrary.BuiltInNames)}). The sound was skipped."));
            return null;
        }

        double seconds;
        try
        {
            seconds = await probe.GetDurationSecondsAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "SFX_UNREADABLE",
                $"Sound \"{name}\" ({Path.GetFileName(path)}) could not be read: {exception.Message} The sound was skipped."));
            return null;
        }

        if (seconds > LongSoundSeconds)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "SFX_LONG",
                $"Sound \"{name}\" is {seconds:0.#} s long; sound effects are meant to be about a second."));
        }

        return (path, Math.Max(1, (long)Math.Ceiling(seconds * fps)));
    }
}
