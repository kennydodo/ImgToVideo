using ImgToVideo.Core.Models;
using ImgToVideo.Core.Overrides;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ProjectOverridesTests
{
    [Fact]
    public void Round_trip_preserves_overrides()
    {
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride
        {
            File = "images/S01_02_CU_ZI.png",
            Motion = MotionType.PanRight,
            Easing = EasingMode.EaseInOut,
            DurationFrames = 150,
            Exclude = false,
            Order = 1,
        });
        overrides.Cuts.Add(new CutOverride
        {
            BeforeFile = "images/S02_01_SCN_ZI.png",
            Transition = TransitionKind.FadeBlack,
        });

        var json = System.Text.Json.JsonSerializer.Serialize(overrides, OverridesJson.JsonOptions);
        var loaded = OverridesJson.LoadFromJson(json);

        var clip = Assert.Single(loaded.Clips);
        Assert.Equal("images/S01_02_CU_ZI.png", clip.File);
        Assert.Equal(MotionType.PanRight, clip.Motion);
        Assert.Equal(EasingMode.EaseInOut, clip.Easing);
        Assert.Equal(150, clip.DurationFrames);
        Assert.False(clip.Exclude);
        Assert.Equal(1, clip.Order);

        var cut = Assert.Single(loaded.Cuts);
        Assert.Equal("images/S02_01_SCN_ZI.png", cut.BeforeFile);
        Assert.Equal(TransitionKind.FadeBlack, cut.Transition);
    }

    [Fact]
    public void Snake_case_keys_are_used()
    {
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride
        {
            File = "a.png",
            Motion = MotionType.Static,
            DurationFrames = 90,
        });

        var json = System.Text.Json.JsonSerializer.Serialize(overrides, OverridesJson.JsonOptions);

        Assert.Contains("\"schema_version\": 1", json);
        Assert.Contains("\"duration_frames\": 90", json);
        Assert.Contains("\"motion\": \"static\"", json);
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        Assert.Throws<InvalidDataException>(
            () => OverridesJson.LoadFromJson("""{ "schema_version": 99 }"""));
    }

    [Fact]
    public void Lookup_helpers_match_case_insensitively()
    {
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride { File = "images/S01_01.png", Motion = MotionType.Static });
        overrides.Cuts.Add(new CutOverride { BeforeFile = "images/S01_02.png", Transition = TransitionKind.FadeBlack });

        Assert.NotNull(overrides.ForClip("IMAGES\\s01_01.PNG"));
        Assert.Equal(TransitionKind.FadeBlack, overrides.CutFor("images/S01_02.png"));
        Assert.Null(overrides.CutFor("images/other.png"));
    }
}
