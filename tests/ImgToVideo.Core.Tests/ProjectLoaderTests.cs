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

        Assert.Contains(inventory.Issues, i => i.Code == "IMAGE_UNRECOGNIZED");
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
        Assert.True(image.Name.HasUnknownCode);
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
