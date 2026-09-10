using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ProjectLoaderTests : IDisposable
{
    private readonly TempProject _project = new();

    [Fact]
    public void Loads_complete_project()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteImage("S01_02_ZI.png", 2304, 1296);
        _project.WriteImage("S02_01.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.False(ValidationIssue.HasErrors(inventory.Issues));
        Assert.NotNull(inventory.AudioFilePath);
        Assert.NotNull(inventory.SrtFilePath);
        Assert.Equal(3, inventory.AllImages.Count);
        Assert.Equal(2, inventory.SceneGroups.Count);
        Assert.Equal("S01", inventory.SceneGroups[0].SceneId);
        Assert.Equal(2, inventory.SceneGroups[0].Images.Count);
        Assert.Equal(4, inventory.Subtitles.Count);
        Assert.Null(inventory.SceneMapWindows);
    }

    [Fact]
    public void Warns_about_unrecognized_files()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteImage("notes.png", 100, 100);
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues,
            i => i.Code == "IMAGE_UNRECOGNIZED" && i.Message.Contains("S{scene}_{index}"));
    }

    [Fact]
    public void Caps_unrecognized_warnings_with_summary()
    {
        for (var i = 1; i <= 7; i++)
        {
            _project.WriteImage($"junk{i}.png", 100, 100);
        }
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Equal(5, inventory.Issues.Count(i => i.Code == "IMAGE_UNRECOGNIZED"));
        Assert.Contains(inventory.Issues,
            i => i.Code == "IMAGE_UNRECOGNIZED_MORE" && i.Message.Contains("2 more"));
    }

    [Fact]
    public void Explains_extension_mismatch_when_no_allowed_extension()
    {
        File.WriteAllBytes(
            System.IO.Path.Combine(_project.Path, "images", "S01_01.jpg"),
            TestImages.Jpeg(2304, 1296));
        File.WriteAllBytes(
            System.IO.Path.Combine(_project.Path, "images", "S01_02.jpg"),
            TestImages.Jpeg(2304, 1296));
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i =>
            i.Code == "IMAGES_EXTENSION" &&
            i.Message.Contains(".jpg") &&
            i.Message.Contains(".png"));
        Assert.Contains(inventory.Issues, i => i.Code == "IMAGES_NONE" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Errors_when_images_folder_is_empty()
    {
        Directory.CreateDirectory(System.IO.Path.Combine(_project.Path, "images"));
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i => i.Code == "IMAGES_EMPTY" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Loads_new_naming_format_with_type_codes()
    {
        _project.WriteImage("S01_01_SCN_ST.png", 2304, 1296);
        _project.WriteImage("S01_02_CU_ZI.png", 2304, 1296);
        _project.WriteImage("S01_03_PROC.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.False(ValidationIssue.HasErrors(inventory.Issues));
        Assert.Equal(3, inventory.AllImages.Count);
        Assert.Equal(ImageType.Scene, inventory.AllImages[0].Name.Type);
        Assert.Equal(ImageType.CloseUp, inventory.AllImages[1].Name.Type);
        Assert.Equal(MotionType.ZoomIn, inventory.AllImages[1].Name.Code);
        Assert.Equal(ImageType.Process, inventory.AllImages[2].Name.Type);
        Assert.Null(inventory.AllImages[2].Name.Code);
    }

    [Fact]
    public void Finds_audio_and_srt_with_any_file_name()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteFile("voice_take3.mp3", "fake audio");
        _project.WriteFile(
            "final_subs.srt",
            "1\n00:00:00,000 --> 00:00:02,000\nHello.\n");

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.False(ValidationIssue.HasErrors(inventory.Issues));
        Assert.EndsWith("voice_take3.mp3", inventory.AudioFilePath);
        Assert.EndsWith("final_subs.srt", inventory.SrtFilePath);
        Assert.DoesNotContain(inventory.Issues, i => i.Code == "AUDIO_AMBIGUOUS");
        Assert.DoesNotContain(inventory.Issues, i => i.Code == "SRT_AMBIGUOUS");
    }

    [Fact]
    public void Info_when_multiple_audio_files_exist()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteFile("audio\\track_b.mp3", "fake audio");
        _project.WriteFile("audio\\track_a.mp3", "fake audio");

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i =>
            i.Code == "AUDIO_AMBIGUOUS" && i.Message.Contains("track_a.mp3"));
        Assert.EndsWith("track_a.mp3", inventory.AudioFilePath);
    }

    [Fact]
    public void Info_when_multiple_srt_files_exist()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteAudio();
        _project.WriteSrt();
        _project.WriteFile(
            "alt.srt",
            "1\n00:00:00,000 --> 00:00:01,000\nAlt.\n");

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i =>
            i.Code == "SRT_AMBIGUOUS" && i.Message.Contains("alt.srt"));
        Assert.Single(inventory.Subtitles);
    }

    [Fact]
    public void Warns_about_unknown_motion_code()
    {
        _project.WriteImage("S01_01_XX.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i => i.Code == "IMAGE_UNKNOWN_CODE");
        var image = Assert.Single(inventory.AllImages);
        Assert.True(image.Name.HasUnknownSuffix);
        Assert.Null(image.Name.Code);
    }

    [Fact]
    public void Errors_when_audio_missing()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteSrt();

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i => i.Code == "AUDIO_MISSING" && i.Severity == ValidationSeverity.Error);
        Assert.Null(inventory.AudioFilePath);
    }

    [Fact]
    public void Errors_when_srt_unparsable()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteAudio();
        _project.WriteFile("narration.srt", "1\nbroken\n");

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.Contains(inventory.Issues, i => i.Code == "SRT_UNPARSABLE" && i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Warns_about_duplicate_stems()
    {
        var options = new ProjectOptions();
        options.Naming.ImageExtensions = [".png", ".jpg"];
        _project.WriteImage("S01_01.png", 2304, 1296);
        File.WriteAllBytes(
            System.IO.Path.Combine(_project.Path, "images", "S01_01.jpg"),
            TestImages.Png(2304, 1296));
        _project.WriteSrt();
        _project.WriteAudio();

        var inventory = ProjectLoader.Load(_project.Path, options);

        Assert.Contains(inventory.Issues, i => i.Code == "IMAGE_DUPLICATE_STEM");
        Assert.Single(inventory.SceneGroups[0].Images);
    }

    [Fact]
    public void Loads_scene_map_when_present()
    {
        _project.WriteImage("S01_01.png", 2304, 1296);
        _project.WriteImage("S02_01.png", 2304, 1296);
        _project.WriteSrt();
        _project.WriteAudio();
        _project.WriteFile(
            "scenes.json",
            """
            [
              { "scene": "S01", "start": 0.0, "end": 10.0 },
              { "scene": "S02", "start": 10.0, "end": 20.0 }
            ]
            """);

        var inventory = ProjectLoader.Load(_project.Path);

        Assert.NotNull(inventory.SceneMapWindows);
        Assert.Equal(2, inventory.SceneMapWindows.Count);
    }

    public void Dispose() => _project.Dispose();
}
