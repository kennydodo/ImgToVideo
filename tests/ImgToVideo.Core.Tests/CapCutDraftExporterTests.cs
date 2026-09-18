using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImgToVideo.CapCut;
using ImgToVideo.Core.Models;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class CapCutDraftExporterTests
{
    private static readonly string Image1Path = @"D:\Repos\My Project\images\S01_01_SCN_ZI.png";
    private static readonly string Image2Path = @"D:\Repos\My Project\images\S01_02_SCN_ST.png";
    private static readonly string AudioPath = @"D:\Repos\My Project\audio\narration.mp3";

    private static (Timeline Timeline, List<ImageInfo> Images) Sample()
    {
        var images = new List<ImageInfo>
        {
            new(Image1Path, new ParsedImageName("S01_01_SCN_ZI", 1, 1, MotionType.ZoomIn, false), 2304, 1296),
            new(Image2Path, new ParsedImageName("S01_02_SCN_ST", 1, 2, MotionType.Static, false), 2304, 1296),
        };

        var timeline = new Timeline
        {
            ProjectName = "Spike Check",
            Resolution = new Resolution(1920, 1080),
            Fps = 30,
            Audio = new AudioTrack { FilePath = AudioPath, DurationFrames = 180 },
            Scenes =
            {
                new Scene
                {
                    Id = "S01",
                    StartFrame = 0,
                    EndFrame = 180,
                    Clips =
                    {
                        new VideoClip
                        {
                            FilePath = Image1Path,
                            SceneId = "S01",
                            StartFrame = 0,
                            DurationFrames = 90,
                            Motion = MotionType.ZoomIn,
                            StartViewport = new Rect(0, 0, 2304, 1296),
                            EndViewport = new Rect(96, 54, 2112, 1188),
                        },
                        new VideoClip
                        {
                            FilePath = Image2Path,
                            SceneId = "S01",
                            StartFrame = 90,
                            DurationFrames = 90,
                            Motion = MotionType.Static,
                            StartViewport = new Rect(0, 0, 2304, 1296),
                            EndViewport = new Rect(0, 0, 2304, 1296),
                        },
                    },
                },
            },
        };

        return (timeline, images);
    }

    private static (JsonNode Content, JsonNode Meta, string Folder) Export()
    {
        var (timeline, images) = Sample();
        var folder = Path.Combine(Path.GetTempPath(), "capcut-export-test", Guid.NewGuid().ToString("N"));
        CapCutDraftExporter.Export(timeline, images, folder);
        var content = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "draft_content.json")))!;
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "draft_meta_info.json")))!;
        return (content, meta, folder);
    }

    [Fact]
    public void Writes_bom_less_files_with_expected_names()
    {
        var (_, _, folder) = Export();

        Assert.True(File.Exists(Path.Combine(folder, "draft_content.json")));
        Assert.True(File.Exists(Path.Combine(folder, "draft_meta_info.json")));
        var bytes = File.ReadAllBytes(Path.Combine(folder, "draft_content.json"));
        Assert.NotEqual(0xEF, bytes[0]); // no UTF-8 BOM — CapCut rejects one
    }

    [Fact]
    public void Duration_and_segment_timeranges_are_microseconds()
    {
        var (content, _, _) = Export();

        Assert.Equal(6000000, (long)content["duration"]!); // 180 frames @ 30 fps
        Assert.Equal(30.0, (double)content["fps"]!);

        var segments = content["tracks"]![0]!["segments"]!.AsArray();
        Assert.Equal(2, segments.Count);
        Assert.Equal(0, (long)segments[0]!["target_timerange"]!["start"]!);
        Assert.Equal(3000000, (long)segments[0]!["target_timerange"]!["duration"]!);
        Assert.Equal(3000000, (long)segments[1]!["target_timerange"]!["start"]!);
        Assert.Equal(3000000, (long)segments[1]!["target_timerange"]!["duration"]!);
    }

    [Fact]
    public void Zoom_in_clip_gets_scale_keyframes_static_does_not()
    {
        var (content, _, _) = Export();

        var segments = content["tracks"]![0]!["segments"]!.AsArray();
        var zoom = segments[0]!.AsObject();
        var keyframes = zoom["common_keyframes"]!.AsArray();
        Assert.Equal(2, keyframes.Count);
        Assert.Equal("KFTypeScaleX", (string?)keyframes[0]!["property_type"]);
        Assert.Equal("KFTypeScaleY", (string?)keyframes[1]!["property_type"]);

        // cover-fit width (1296 * 16/9 = 2304) over viewports: 2304/2304 -> 2304/2112
        var values = keyframes[0]!["keyframe_list"]!.AsArray()
            .Select(k => (double)k!["values"]![0]!).ToArray();
        Assert.Equal(1.0, values[0], precision: 6);
        Assert.Equal(2304.0 / 2112.0, values[1], precision: 6);
        Assert.Equal(0, (long)keyframes[0]!["keyframe_list"]![0]!["time_offset"]!);
        Assert.Equal(3000000, (long)keyframes[0]!["keyframe_list"]![1]!["time_offset"]!);

        var staticClip = segments[1]!.AsObject();
        Assert.Empty(staticClip["common_keyframes"]!.AsArray());
    }

    [Fact]
    public void Every_segment_carries_six_companion_material_refs()
    {
        var (content, _, _) = Export();

        var materials = content["materials"]!;
        foreach (var kind in new[] { "canvases", "speeds", "sound_channel_mappings", "material_colors", "placeholder_infos", "vocal_separations" })
        {
            Assert.True(materials[kind]!.AsArray().Count >= 2, kind);
        }

        foreach (var segment in content["tracks"]![0]!["segments"]!.AsArray())
        {
            Assert.Equal(6, segment!["extra_material_refs"]!.AsArray().Count);
        }
    }

    [Fact]
    public void Media_materials_reference_absolute_paths_and_audio_track_has_narration()
    {
        var (content, _, _) = Export();

        var videos = content["materials"]!["videos"]!.AsArray();
        Assert.Equal(2, videos.Count);
        Assert.Contains(videos, v => ((string?)v!["path"])!.Contains("S01_01_SCN_ZI.png"));
        Assert.All(videos, v => Assert.Equal(10800000000, (long)v!["duration"]!));

        var audioTrack = content["tracks"]![1]!;
        Assert.Equal("audio", (string?)audioTrack["type"]);
        var audioSegment = audioTrack["segments"]!.AsArray()[0]!;
        Assert.Equal(6000000, (long)audioSegment["target_timerange"]!["duration"]!);
        var audioMaterial = content["materials"]!["audios"]!.AsArray()[0]!;
        Assert.Contains("narration.mp3", (string?)audioMaterial["path"]);
        Assert.True((bool)audioMaterial["has_audio"]!);
    }

    [Fact]
    public void Meta_info_carries_name_paths_and_duration()
    {
        var (content, meta, folder) = Export();

        Assert.NotNull(meta["draft_name"]);
        Assert.Equal(6000000, (long)meta["tm_duration"]!);
        Assert.Contains(folder.Replace('\\', '/'), (string?)meta["draft_fold_path"]);
        Assert.Equal("185.0.0", (string?)meta["draft_new_version"]);
        var value = meta["draft_materials"]!.AsArray()[0]!["value"]!.AsArray();
        Assert.Equal(2, value.Count);
        Assert.Contains(value, v => ((string?)v!["file_Path"])!.Contains("S01_02_SCN_ST.png"));
    }
}
