using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ProjectOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(new ProjectOptions().Validate());
    }

    [Fact]
    public void Invalid_timing_order_is_reported()
    {
        var options = new ProjectOptions();
        options.Timing.MinImageSeconds = 6.0;
        options.Timing.PreferredImageSeconds = 5.0;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.Contains("floor <= min <= preferred <= max"));
    }

    [Fact]
    public void Inverted_push_in_is_reported()
    {
        var options = new ProjectOptions();
        options.Motion.PushInEndPercent = 99.0;

        Assert.Contains(options.Validate(), e => e.Contains("push-in"));
    }

    [Fact]
    public void Odd_output_dimensions_are_reported()
    {
        var options = new ProjectOptions();
        options.Output.Width = 1919;

        Assert.Contains(options.Validate(), e => e.Contains("even"));
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var options = new ProjectOptions();
        options.Timing.PreferredImageSeconds = 4.5;
        options.Motion.AutoMotionEnabled = false;
        options.Transitions.Enabled = false;
        options.Transitions.Kind = TransitionKind.None;
        options.Naming.ScenePrefix = "SC";
        options.Naming.Separator = "-";
        options.Naming.NumberPadding = 3;
        options.Render.PreviewCrf = 30;

        var json = System.Text.Json.JsonSerializer.Serialize(options, OptionsJson.JsonOptions);
        var loaded = OptionsJson.LoadFromJson(json);

        Assert.Equal(4.5, loaded.Timing.PreferredImageSeconds);
        Assert.False(loaded.Motion.AutoMotionEnabled);
        Assert.False(loaded.Transitions.Enabled);
        Assert.Equal(TransitionKind.None, loaded.Transitions.Kind);
        Assert.Equal("SC", loaded.Naming.ScenePrefix);
        Assert.Equal("-", loaded.Naming.Separator);
        Assert.Equal(3, loaded.Naming.NumberPadding);
        Assert.Equal(30, loaded.Render.PreviewCrf);
    }

    [Fact]
    public void Partial_json_keeps_defaults_for_missing_fields()
    {
        var loaded = OptionsJson.LoadFromJson("""{ "schema_version": 1, "timing": { "preferred_image_seconds": 4.0 } }""");

        Assert.Equal(4.0, loaded.Timing.PreferredImageSeconds);
        Assert.Equal(2.5, loaded.Timing.MinImageSeconds);
        Assert.True(loaded.Motion.AutoMotionEnabled);
        Assert.Equal("S", loaded.Naming.ScenePrefix);
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        Assert.Throws<InvalidDataException>(
            () => OptionsJson.LoadFromJson("""{ "schema_version": 99 }"""));
    }

    [Fact]
    public void Missing_schema_version_is_rejected()
    {
        Assert.Throws<InvalidDataException>(
            () => OptionsJson.LoadFromJson("""{ "timing": {} }"""));
    }

    [Fact]
    public void Save_then_load_is_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"imgtovideo-options-{Guid.NewGuid():N}.json");
        try
        {
            var options = new ProjectOptions();
            options.Render.PreviewHeight = 480;

            OptionsJson.Save(options, path);
            var loaded = OptionsJson.Load(path);

            Assert.Equal(480, loaded.Render.PreviewHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Serializes_snake_case_keys()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new ProjectOptions(), OptionsJson.JsonOptions);

        Assert.Contains("\"schema_version\": 1", json);
        Assert.Contains("\"auto_motion_enabled\": true", json);
        Assert.Contains("\"scene_prefix\": \"S\"", json);
    }
}
