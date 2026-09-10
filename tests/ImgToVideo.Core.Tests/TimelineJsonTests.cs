using ImgToVideo.Core.Models;
using ImgToVideo.Core.Serialization;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class TimelineJsonTests
{
    private static Timeline SampleTimeline()
    {
        var timeline = new Timeline
        {
            ProjectName = "Japanese Home Rules",
            Resolution = new Resolution(1920, 1080),
            Fps = 30.0,
            Audio = new AudioTrack
            {
                FilePath = "audio/narration.mp3",
                DurationFrames = 30 * 638,
            },
        };

        var scene = new Scene
        {
            Id = "S01",
            StartFrame = 0,
            EndFrame = 30 * 9,
            Clips =
            {
                        new VideoClip
                        {
                            FilePath = "images/S01_01.png",
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = 135,
                            Motion = MotionType.ZoomIn,
                            MotionSource = MotionSource.ExplicitCode,
                            ImageType = ImageType.Scene,
                            StartViewport = new Rect(0, 0, 2304, 1296),
                            EndViewport = new Rect(60, 30, 2160, 1215),
                        },
                new VideoClip
                {
                    FilePath = "images/S01_02.png",
                    SceneId = "S01",
                    StartFrame = 135,
                    DurationFrames = 135,
                    Motion = MotionType.PanRight,
                    MotionSource = MotionSource.AutoSelected,
                    StartViewport = new Rect(0, 0, 2400, 1350),
                    EndViewport = new Rect(480, 0, 2400, 1350),
                    Transition = new TransitionIn
                    {
                        Kind = TransitionKind.Crossfade,
                        DurationFrames = 15,
                    },
                },
            },
        };

        timeline.Scenes.Add(scene);
        return timeline;
    }

    [Fact]
    public void Round_trip_preserves_frames_motion_and_viewports()
    {
        var original = SampleTimeline();

        var json = System.Text.Json.JsonSerializer.Serialize(original, TimelineJson.Options);
        var loaded = TimelineJson.LoadFromJson(json);

        Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(original.ProjectName, loaded.ProjectName);
        Assert.Equal(original.Fps, loaded.Fps);
        Assert.Equal(original.Audio.DurationFrames, loaded.Audio.DurationFrames);

        var originalClip = original.Scenes[0].Clips[0];
        var loadedClip = loaded.Scenes[0].Clips[0];
        Assert.Equal(originalClip.FilePath, loadedClip.FilePath);
        Assert.Equal(originalClip.StartFrame, loadedClip.StartFrame);
        Assert.Equal(originalClip.DurationFrames, loadedClip.DurationFrames);
        Assert.Equal(MotionType.ZoomIn, loadedClip.Motion);
        Assert.Equal(MotionSource.ExplicitCode, loadedClip.MotionSource);
        Assert.Equal(ImageType.Scene, loadedClip.ImageType);
        Assert.Equal(originalClip.StartViewport, loadedClip.StartViewport);
        Assert.Equal(originalClip.EndViewport, loadedClip.EndViewport);

        var originalPan = original.Scenes[0].Clips[1];
        var loadedPan = loaded.Scenes[0].Clips[1];
        Assert.Equal(MotionType.PanRight, loadedPan.Motion);
        Assert.Equal(MotionSource.AutoSelected, loadedPan.MotionSource);
        Assert.NotNull(loadedPan.Transition);
        Assert.Equal(TransitionKind.Crossfade, loadedPan.Transition!.Kind);
        Assert.Equal(15, loadedPan.Transition.DurationFrames);
    }

    [Fact]
    public void Save_is_byte_identical_across_runs()
    {
        var json1 = System.Text.Json.JsonSerializer.Serialize(SampleTimeline(), TimelineJson.Options);
        var json2 = System.Text.Json.JsonSerializer.Serialize(SampleTimeline(), TimelineJson.Options);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Load_rejects_unknown_schema_version()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(SampleTimeline(), TimelineJson.Options);
        var mutated = json.Replace("\"schema_version\": 2", "\"schema_version\": 99");

        Assert.Throws<InvalidDataException>(() => TimelineJson.LoadFromJson(mutated));
    }

    [Fact]
    public void Load_rejects_missing_schema_version()
    {
        Assert.Throws<InvalidDataException>(() => TimelineJson.LoadFromJson("{ \"projectName\": \"x\" }"));
    }

    [Fact]
    public void Load_rejects_unknown_motion_string()
    {
        var json = """
            {
              "schema_version": 2,
              "project_name": "x",
              "resolution": { "width": 1920, "height": 1080 },
              "fps": 30,
              "audio": { "file_path": "a.mp3", "duration_frames": 300 },
              "scenes": [
                {
                  "id": "S01",
                  "start_frame": 0,
                  "end_frame": 300,
                  "clips": [
                    {
                      "file_path": "images/S01_01.png",
                      "scene_id": "S01",
                      "start_frame": 0,
                      "duration_frames": 300,
                      "motion": "wiggle",
                      "motion_source": "auto_selected",
                      "start_viewport": { "x": 0, "y": 0, "width": 100, "height": 100 },
                      "end_viewport": { "x": 0, "y": 0, "width": 100, "height": 100 }
                    }
                  ]
                }
              ]
            }
            """;

        Assert.Throws<System.Text.Json.JsonException>(() => TimelineJson.LoadFromJson(json));
    }

    [Fact]
    public void Serializes_motion_as_snake_case()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(SampleTimeline(), TimelineJson.Options);

        Assert.Contains("\"motion\": \"zoom_in\"", json);
        Assert.Contains("\"motion_source\": \"explicit_code\"", json);
        Assert.Contains("\"schema_version\": 2", json);
        Assert.Contains("\"image_type\": \"scene\"", json);
    }
}
