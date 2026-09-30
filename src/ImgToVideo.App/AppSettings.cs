using System.IO;
using System.Text.Json;

namespace ImgToVideo.App;

public sealed class AppSettings
{
    public string LastProjectFolder { get; set; } = string.Empty;

    /// <summary>
    /// The user's own permanent default output resolution, saved from the
    /// Settings window's "Save as default" button next to the resolution
    /// preset. Null until the user ever saves one - new projects then keep
    /// falling back to OutputOptions' own default (2560x1440). This is a
    /// per-user preference (this file), not a per-project setting
    /// (imgtovideo.json) - it only seeds the Output section of a brand new
    /// project; it never touches a project that already has its own
    /// imgtovideo.json.
    /// </summary>
    public int? DefaultOutputWidth { get; set; }
    public int? DefaultOutputHeight { get; set; }
}

public static class AppSettingsStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImgToVideo", "app.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
    }
}
