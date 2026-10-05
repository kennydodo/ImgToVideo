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
        Assert.Equal(("0", "100"), scale[0]);
        Assert.Equal(("89", "106.666667"), scale[^1]);
        Assert.Equal(scale, scale.OrderBy(k => long.Parse(k.When)));

        var center = CenterKeyframes(clipItem);
        Assert.Equal(6, center.Length);
        Assert.All(center, k => Assert.Equal(("0", "0"), (k.Horiz, k.Vert)));
    }

    [Fact]
    public void Pan_right_motion_keyframes_slide_image_opposite_to_viewport()
    {
        var (timeline, images) = Sample();
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var clipItem = ClipItem(doc, "clipitem-2");

        var scale = ScalarKeyframes(clipItem, "scale");
        Assert.Equal(6, scale.Length);
        Assert.All(scale, k => Assert.Equal("100", k.Value));

        // Center is media-size-relative: the viewport pans right, so the
        // image slides left from +480/2880 to -480/2880 native units.
        var center = CenterKeyframes(clipItem);
        Assert.Equal(6, center.Length);
        Assert.Equal(("0", "0.166667", "0"), center[0]);
        Assert.Equal(("89", "-0.166667", "0"), center[^1]);
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
    public void Crossfade_join_uses_the_standard_fcp7_opacity_effect()
    {
        // Mirrors Premiere's own serialization (verified against a reference
        // export): the opacity effect lives in its own filter with effecttype
        // "motion" and a single "opacity" parameter. The FCP7 spec's "level"
        // parameterid is Premiere's AUDIO levels parameter — video opacity
        // ignores it, which made every crossfade import as a hard cut.
        var (timeline, images) = Sample();
        timeline.Scenes[0].Clips[1].Transition = new TransitionIn
        {
            Kind = TransitionKind.Crossfade,
            DurationFrames = 15,
        };
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var overlay = ClipItem(doc, "clipitem-2-x");
        Assert.Equal("S01_02_PR.png", overlay.Element("name")?.Value);
        Assert.Equal("75", overlay.Element("start")?.Value); // 15 frames before the cut
        Assert.Equal("90", overlay.Element("end")?.Value);   // trimmed to the fade window
        Assert.Equal("15", overlay.Element("out")?.Value);

        // Frozen motion: a single keyframe on the clip's first framing — V1
        // animates the real motion once the cut lands.
        var scale = ScalarKeyframes(overlay, "scale");
        Assert.Single(scale);
        Assert.Equal(("0", "100"), scale[0]);

        // The opacity effect sits in its own filter element.
        var filters = overlay.Elements("filter").ToList();
        Assert.Equal(2, filters.Count);
        var opacityEffect = filters[1].Element("effect")!;
        Assert.Equal("opacity", opacityEffect.Element("effectid")?.Value);
        Assert.Equal("motion", opacityEffect.Element("effecttype")?.Value);

        var parameter = opacityEffect.Element("parameter")!;
        Assert.Equal("opacity", parameter.Element("parameterid")?.Value);
        var keyframes = parameter.Elements("keyframe")
            .Select(k => ((string?)k.Element("when") ?? "", (string?)k.Element("value") ?? ""))
            .ToArray();
        Assert.Equal(2, keyframes.Length);
        Assert.Equal(("0", "0"), keyframes[0]);
        Assert.Equal(("15", "100"), keyframes[1]);

        // The basic-motion filter carries no opacity parameter.
        var basic = filters[0].Descendants("effect")
            .First(e => (string?)e.Element("effectid") == "basic");
        Assert.DoesNotContain(
            basic.Descendants("parameter"),
            p => (string?)p.Element("parameterid") == "opacity");
    }

    [Fact]
    public void Dip_join_is_sequential_on_v1_without_overlays()
    {
        // A dip fades the outgoing tail to black and the incoming head in from
        // it — sequential on V1, no overlay copies, no opacity on the wrong clip.
        var (timeline, images) = Sample();
        timeline.Scenes[0].Clips[1].Transition = new TransitionIn
        {
            Kind = TransitionKind.FadeBlack,
            DurationFrames = 15,
        };
        var doc = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images));

        var videoClips = doc.Descendants("clipitem")
            .Where(c => c.Element("sourcetrack") is null)
            .ToList();
        Assert.Equal(2, videoClips.Count);

        // Outgoing tail: 100 at frame 75 (90 - 15) -> 0 at the cut.
        var outgoing = ScalarKeyframes(ClipItem(doc, "clipitem-1"), "opacity");
        Assert.Equal(2, outgoing.Length);
        Assert.Equal(("75", "100"), outgoing[0]);
        Assert.Equal(("90", "0"), outgoing[^1]);

        // Incoming head: 0 at its start -> 100 at frame 15.
        var incoming = ScalarKeyframes(ClipItem(doc, "clipitem-2"), "opacity");
        Assert.Equal(2, incoming.Length);
        Assert.Equal(("0", "0"), incoming[0]);
        Assert.Equal(("15", "100"), incoming[^1]);
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
