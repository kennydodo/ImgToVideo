using System.Xml.Linq;
using ImgToVideo.Core.Models;
using ImgToVideo.Premiere;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class Fcp7XmlExporterTests
{
    private static readonly string Image1Path = @"D:\Repos\My Project\images\S01_01.png";
    private static readonly string Image2Path = @"D:\Repos\My Project\images\S01_02_PR.png";
    private static readonly string AudioPath = @"D:\Repos\My Project\audio\narration.mp3";

    private static (Timeline Timeline, List<ImageInfo> Images) Sample()
    {
        var images = new List<ImageInfo>
        {
            new(Image1Path, new ParsedImageName("S01_01", 1, 1, null, false), 2304, 1296),
            new(Image2Path, new ParsedImageName("S01_02_PR", 1, 2, MotionType.PanRight, false), 2880, 1296),
        };

        var timeline = new Timeline
        {
            ProjectName = "Japanese Home Rules",
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
                            StartViewport = new Rect(192, 108, 1920, 1080),
                            EndViewport = new Rect(252, 141.75, 1800, 1012.5),
                        },
                        new VideoClip
                        {
                            FilePath = Image2Path,
                            SceneId = "S01",
                            StartFrame = 90,
                            DurationFrames = 90,
                            Motion = MotionType.PanRight,
                            MotionSource = MotionSource.ExplicitCode,
                            StartViewport = new Rect(0, 108, 1920, 1080),
                            EndViewport = new Rect(960, 108, 1920, 1080),
                        },
                    },
                },
            },
        };

        return (timeline, images);
    }

    private static XElement ClipItem(XDocument doc, string id) =>
        doc.Descendants("clipitem").First(c => (string?)c.Attribute("id") == id);

    private static (string When, string Value)[] ScalarKeyframes(XElement clipItem, string parameterId) =>
        clipItem.Descendants("parameter")
            .First(p => (string?)p.Element("parameterid") == parameterId)
            .Elements("keyframe")
            .Select(k => ((string?)k.Element("when") ?? "", (string?)k.Element("value") ?? ""))
            .ToArray();

    private static (string When, string Horiz, string Vert)[] CenterKeyframes(XElement clipItem)
    {
        var parameter = clipItem.Descendants("parameter")
            .First(p => (string?)p.Element("parameterid") == "center");
        return parameter.Elements("keyframe")
            .Select(k => (
                (string?)k.Element("when") ?? "",
                (string?)k.Element("value")?.Element("horiz") ?? "",
                (string?)k.Element("value")?.Element("vert") ?? ""))
            .ToArray();
    }

    [Fact]
    public void Produces_xmeml_with_sequence_metadata()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var root = doc.Root!;
        Assert.Equal("xmeml", root.Name.LocalName);
        Assert.Equal("5", root.Attribute("version")?.Value);

        var sequence = root.Element("sequence")!;
        Assert.Equal("Japanese Home Rules", sequence.Element("name")?.Value);
        Assert.Equal("180", sequence.Element("duration")?.Value);
        Assert.Equal("30", sequence.Element("rate")?.Element("timebase")?.Value);
        Assert.Equal("1920", sequence.Element("media")?.Element("video")?.Element("format")?
            .Element("samplecharacteristics")?.Element("width")?.Value);
        Assert.Equal("1080", sequence.Element("media")?.Element("video")?.Element("format")?
            .Element("samplecharacteristics")?.Element("height")?.Value);
    }

    [Fact]
    public void Video_track_has_one_clipitem_per_clip_in_timeline_order()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var clipItems = doc.Descendants("clipitem")
            .Where(c => c.Element("sourcetrack") is null)
            .ToList();

        Assert.Equal(2, clipItems.Count);

        var first = clipItems[0];
        Assert.Equal("S01_01.png", first.Element("name")?.Value);
        Assert.Equal("0", first.Element("start")?.Value);
        Assert.Equal("90", first.Element("end")?.Value);
        Assert.Equal("0", first.Element("in")?.Value);
        Assert.Equal("90", first.Element("out")?.Value);
        Assert.Equal("2304", first.Element("file")?.Element("media")?.Element("video")?
            .Element("samplecharacteristics")?.Element("width")?.Value);

        var second = clipItems[1];
        Assert.Equal("S01_02_PR.png", second.Element("name")?.Value);
        Assert.Equal("90", second.Element("start")?.Value);
        Assert.Equal("180", second.Element("end")?.Value);
        Assert.Equal("2880", second.Element("file")?.Element("media")?.Element("video")?
            .Element("samplecharacteristics")?.Element("width")?.Value);
    }

    [Fact]
    public void Pathurl_uses_file_uri_with_forward_slashes_and_escaped_spaces()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var pathUrl = ClipItem(doc, "clipitem-1").Element("file")?.Element("pathurl")?.Value;

        Assert.Equal("file://localhost/D:/Repos/My%20Project/images/S01_01.png", pathUrl);
    }

    [Fact]
    public void Zoom_in_motion_keyframes_match_viewport_model()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var clipItem = ClipItem(doc, "clipitem-1");

        // Eased motion is sampled (~2 keyframes/second): 90 frames @ 30 fps -> 6 samples.
        var scale = ScalarKeyframes(clipItem, "scale");
        Assert.Equal(6, scale.Length);
        Assert.Equal(("0", "120"), scale[0]);
        Assert.Equal(("89", "128"), scale[^1]);
        Assert.Equal(scale, scale.OrderBy(k => long.Parse(k.When)));

        var center = CenterKeyframes(clipItem);
        Assert.Equal(6, center.Length);
        Assert.All(center, k => Assert.Equal(("960", "540"), (k.Horiz, k.Vert)));
    }

    [Fact]
    public void Pan_right_motion_keyframes_slide_image_opposite_to_viewport()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var clipItem = ClipItem(doc, "clipitem-2");

        var scale = ScalarKeyframes(clipItem, "scale");
        Assert.Equal(6, scale.Length);
        Assert.All(scale, k => Assert.Equal("150", k.Value));

        var center = CenterKeyframes(clipItem);
        Assert.Equal(6, center.Length);
        Assert.Equal(("0", "1440", "540"), center[0]);
        Assert.Equal(("89", "480", "540"), center[^1]);
        var horiz = center.Select(k => double.Parse(k.Horiz, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(horiz, horiz.OrderByDescending(v => v).ToArray());
    }

    [Fact]
    public void Motion_effect_is_wrapped_in_a_filter()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var clipItem = ClipItem(doc, "clipitem-1");

        Assert.NotNull(clipItem.Element("filter")?.Element("effect"));
        Assert.Equal("basic", clipItem.Element("filter")?.Element("effect")?.Element("effectid")?.Value);
    }

    [Fact]
    public void Cuts_only_fallback_omits_motion_effects()
    {
        var (timeline, images) = Sample();
        var xml = Fcp7XmlExporter.Export(timeline, images,
            new PremiereExportOptions { IncludeMotionKeyframes = false });
        var doc = XDocument.Parse(xml);

        Assert.DoesNotContain(doc.Descendants("effect"), e => (string?)e.Element("effectid") == "basic");
    }

    [Fact]
    public void Audio_track_spans_timeline_with_sourcetrack()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var audioClipItem = ClipItem(doc, "clipitem-audio-1");
        Assert.Equal("narration.mp3", audioClipItem.Element("name")?.Value);
        Assert.Equal("0", audioClipItem.Element("start")?.Value);
        Assert.Equal("180", audioClipItem.Element("end")?.Value);
        Assert.Equal("audio", audioClipItem.Element("sourcetrack")?.Element("mediatype")?.Value);
        Assert.Contains("narration.mp3", audioClipItem.Element("file")?.Element("pathurl")?.Value);
    }

    [Fact]
    public void Special_characters_in_names_are_escaped()
    {
        var (timeline, images) = Sample();
        timeline.ProjectName = "A & B <Test>";
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        Assert.Equal("A & B <Test>", doc.Root?.Element("sequence")?.Element("name")?.Value);
    }
}
