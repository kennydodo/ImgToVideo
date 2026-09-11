using ImgToVideo.Core.Imaging;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using ImgToVideo.Core.Parsing;
using System.Text.Json;

namespace ImgToVideo.Core.Analysis;

public sealed class ProjectInventory
{
    public string ProjectFolder { get; init; } = string.Empty;
    public string? AudioFilePath { get; init; }
    public string? SrtFilePath { get; init; }
    public List<ImageInfo> AllImages { get; init; } = new();
    public List<SceneImageGroup> SceneGroups { get; init; } = new();
    public List<SubtitleBlock> Subtitles { get; init; } = new();
    public IReadOnlyList<SceneWindow>? SceneMapWindows { get; init; }
    public ProjectOverrides Overrides { get; init; } = new();
    public List<ValidationIssue> Issues { get; init; } = new();
}

public static class ProjectLoader
{
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".flac"];

    public static ProjectInventory Load(string projectFolder, ProjectOptions? options = null)
    {
        options ??= new ProjectOptions();
        var issues = new List<ValidationIssue>();

        var audio = FindAudio(projectFolder, issues);
        var srt = FindSrt(projectFolder, issues);
        var subtitles = ParseSubtitles(srt, issues);
        var overrides = LoadOverrides(projectFolder, issues);
        var images = LoadImages(projectFolder, options.Naming, overrides, issues);
        var sceneMap = LoadSceneMap(projectFolder, issues);

        return new ProjectInventory
        {
            ProjectFolder = projectFolder,
            AudioFilePath = audio,
            SrtFilePath = srt,
            Subtitles = subtitles,
            AllImages = images.All,
            SceneGroups = images.Groups,
            SceneMapWindows = sceneMap,
            Overrides = overrides,
            Issues = issues,
        };
    }

    private static ProjectOverrides LoadOverrides(string projectFolder, List<ValidationIssue> issues)
    {
        ProjectOverrides overrides;
        try
        {
            overrides = OverridesJson.LoadOrDefault(Path.Combine(projectFolder, "overrides.json"));
        }
        catch (InvalidDataException e)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "OVERRIDES_INVALID",
                $"overrides.json could not be loaded: {e.Message}"));
            return new ProjectOverrides();
        }
        catch (JsonException e)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "OVERRIDES_INVALID",
                $"overrides.json could not be loaded: {e.Message}"));
            return new ProjectOverrides();
        }

        foreach (var clip in overrides.Clips)
        {
            clip.File = ResolveProjectPath(projectFolder, clip.File);
        }

        foreach (var cut in overrides.Cuts)
        {
            cut.BeforeFile = ResolveProjectPath(projectFolder, cut.BeforeFile);
        }

        return overrides;
    }

    private static string ResolveProjectPath(string projectFolder, string file) =>
        Path.IsPathRooted(file) ? file : Path.GetFullPath(Path.Combine(projectFolder, file));

    private static string? FindAudio(string projectFolder, List<ValidationIssue> issues)
    {
        var audioDir = Path.Combine(projectFolder, "audio");
        foreach (var dir in new[] { audioDir, projectFolder })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var candidates = Directory.EnumerateFiles(dir)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f), NaturalSortComparer.Instance)
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "AUDIO_AMBIGUOUS",
                    $"Multiple audio files found in \"{Path.GetFileName(dir)}\"; " +
                    $"using \"{Path.GetFileName(candidates[0])}\"."));
            }

            return candidates[0];
        }

        issues.Add(new ValidationIssue(
            ValidationSeverity.Error, "AUDIO_MISSING",
            "No audio file (.mp3, .wav, …) found in the \"audio\" folder or the project root."));
        return null;
    }

    private static string? FindSrt(string projectFolder, List<ValidationIssue> issues)
    {
        if (Directory.Exists(projectFolder))
        {
            var candidates = Directory.EnumerateFiles(projectFolder, "*.srt")
                .OrderBy(f => Path.GetFileName(f), NaturalSortComparer.Instance)
                .ToList();

            if (candidates.Count > 1)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Info, "SRT_AMBIGUOUS",
                    $"Multiple subtitle files found; using \"{Path.GetFileName(candidates[0])}\"."));
            }

            if (candidates.Count > 0)
            {
                return candidates[0];
            }
        }

        issues.Add(new ValidationIssue(
            ValidationSeverity.Error, "SRT_MISSING",
            "No subtitle file (.srt) found in the project folder."));
        return null;
    }

    private static List<SubtitleBlock> ParseSubtitles(string? srtPath, List<ValidationIssue> issues)
    {
        if (srtPath is null)
        {
            return [];
        }

        try
        {
            var blocks = SrtParser.Parse(File.ReadAllText(srtPath));
            if (blocks.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error, "SRT_EMPTY",
                    $"\"{Path.GetFileName(srtPath)}\" contains no subtitle blocks."));
            }

            return blocks;
        }
        catch (FormatException e)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SRT_UNPARSABLE",
                $"\"{Path.GetFileName(srtPath)}\" could not be parsed: {e.Message}"));
            return [];
        }
        catch (IOException e)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "SRT_UNREADABLE",
                $"\"{Path.GetFileName(srtPath)}\" could not be read: {e.Message}"));
            return [];
        }
    }

    private static (List<ImageInfo> All, List<SceneImageGroup> Groups) LoadImages(
        string projectFolder, NamingOptions naming, ProjectOverrides overrides, List<ValidationIssue> issues)
    {
        var imagesDir = Path.Combine(projectFolder, "images");
        if (!Directory.Exists(imagesDir))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "IMAGES_MISSING",
                "No \"images\" folder found in the project folder."));
            return ([], []);
        }

        var allowedExtensions = naming.ImageExtensions
            .Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parser = new ImageFilenameParser(naming);
        var allFiles = Directory.EnumerateFiles(imagesDir)
            .OrderBy(f => Path.GetFileName(f), NaturalSortComparer.Instance)
            .ToList();

        if (allFiles.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "IMAGES_EMPTY",
                "The \"images\" folder is empty — it contains no files at all."));
            return ([], []);
        }

        var files = allFiles
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        if (files.Count == 0)
        {
            var presentExtensions = allFiles
                .Select(f => Path.GetExtension(f) ?? string.Empty)
                .Where(e => e.Length > 0)
                .Select(e => e.ToLowerInvariant())
                .Distinct()
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToList();
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "IMAGES_EXTENSION",
                $"Found {allFiles.Count} files in \"images\" but none has an allowed extension " +
                $"({string.Join(", ", naming.ImageExtensions)}). Extensions present: " +
                $"{string.Join(", ", presentExtensions)}. " +
                "Add them in Settings > File naming > Extensions."));
        }

        const int MaxShownUnrecognized = 5;
        var unrecognizedShown = 0;
        foreach (var file in files)
        {
            if (!parser.TryParse(file, out var parsed) || parsed is null)
            {
                unrecognizedShown++;
                if (unrecognizedShown <= MaxShownUnrecognized)
                {
                    issues.Add(new ValidationIssue(
                        ValidationSeverity.Warning, "IMAGE_UNRECOGNIZED",
                        $"\"{Path.GetFileName(file)}\" does not match the naming pattern " +
                        "(expected S{scene}_{index}[_CODE].png, e.g. S08_02_PR.png) and was ignored."));
                }
            }
            else if (parsed.HasUnknownSuffix)
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "IMAGE_UNKNOWN_CODE",
                    $"\"{Path.GetFileName(file)}\" has an unknown suffix; motion will be auto-selected."));
            }
        }

        if (unrecognizedShown > MaxShownUnrecognized)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning, "IMAGE_UNRECOGNIZED_MORE",
                $"…and {unrecognizedShown - MaxShownUnrecognized} more files were ignored for the same reason."));
        }

        var images = new List<ImageInfo>();
        var seenStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!parser.TryParse(file, out var parsed) || parsed is null)
            {
                continue;
            }

            var stemKey = Path.GetFileNameWithoutExtension(file);
            if (!seenStems.Add(stemKey))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "IMAGE_DUPLICATE_STEM",
                    $"\"{Path.GetFileName(file)}\" duplicates an earlier stem; only the first file is used."));
                continue;
            }

            if (!ImageDimensionsReader.TryRead(file, out var width, out var height))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning, "IMAGE_UNREADABLE",
                    $"\"{Path.GetFileName(file)}\" has unreadable image dimensions."));
            }

            images.Add(new ImageInfo(file, parsed, width, height));
        }

        foreach (var image in images.Where(i => overrides.ForClip(i.FilePath)?.Exclude == true).ToList())
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Info, "IMAGE_EXCLUDED",
                $"\"{Path.GetFileName(image.FilePath)}\" is excluded by overrides and was skipped."));
            images.Remove(image);
        }

        if (images.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error, "IMAGES_NONE",
                "No correctly named images were found in the \"images\" folder. " +
                "Names must look like S01_01.png or S08_02_SCN_PR.png " +
                "(pattern: S{scene}_{index}[_TYPE][_CODE].png)."));
        }

        var orderLookup = overrides.Clips
            .Where(c => c.Order is not null)
            .ToDictionary(c => c.File, c => c.Order!.Value, StringComparer.OrdinalIgnoreCase);

        images = images
            .OrderBy(i => orderLookup.TryGetValue(i.FilePath, out var order) ? order : int.MaxValue)
            .ToList();

        var groups = images
            .GroupBy(i => i.Name.SceneNumber)
            .OrderBy(g => g.Key)
            .Select(g => new SceneImageGroup(g.Key, naming.SceneId(g.Key), g.ToList()))
            .ToList();

        return (images, groups);
    }

    private static IReadOnlyList<SceneWindow>? LoadSceneMap(string projectFolder, List<ValidationIssue> issues)
    {
        var path = Path.Combine(projectFolder, "scenes.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var result = SceneMapParser.ParseFile(path);
        issues.AddRange(result.Issues);
        return result.Windows;
    }
}
