using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Serialization;

namespace ImgToVideo.App;

public sealed class AppSettings
{
    public string LastProjectFolder { get; set; } = string.Empty;

    /// <summary>
    /// The user's own permanent defaults, saved from the "Save as my
    /// default" button next to each Settings section (Output > Resolution,
    /// Motion, Transitions). Null until the user ever saves one - new
    /// projects then keep falling back to that section's own built-in
    /// default (ProjectOptions' own values). This is a per-user preference
    /// (this file), not a per-project setting (imgtovideo.json) - it only
    /// seeds a BRAND NEW project; it never touches a project that already
    /// has its own imgtovideo.json.
    /// </summary>
    public int? DefaultOutputWidth { get; set; }
    public int? DefaultOutputHeight { get; set; }
    public MotionOptions? DefaultMotion { get; set; }
    public TransitionOptions? DefaultTransitions { get; set; }
}

public static class AppSettingsStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImgToVideo", "app.json");

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (IOException)
        {
        }
    }

    // Same enum converters imgtovideo.json uses (OptionsJson.CreateOptions),
    // so a saved DefaultMotion/DefaultTransitions reads as "EaseInOut" /
    // "Crossfade" in app.json rather than a bare int - this file is meant to
    // be human-inspectable like the project options file is.
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new TransitionKindJsonConverter());
        options.Converters.Add(new TransitionAlignmentJsonConverter());
        options.Converters.Add(new EasingModeJsonConverter());
        return options;
    }
}
