using System.Globalization;
using System.Xml.Linq;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Premiere;

public sealed record PremiereExportOptions
{
    public bool IncludeMotionKeyframes { get; init; } = true;
}

public static class Fcp7XmlExporter
{
    public static string Export(
        Timeline timeline,
        IReadOnlyList<ImageInfo> images,
        PremiereExportOptions? options = null)
    {
        options ??= new PremiereExportOptions();

        var dimensions = images.ToDictionary(i => i.FilePath, i => (Width: i.Width, Height: i.Height));
        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        if (clips.Count == 0)
        {
            throw new InvalidOperationException("Timeline has no clips to export.");
        }

        var timebase = (int)Math.Round(timeline.Fps);
        var totalFrames = clips.Sum(c => c.DurationFrames);
        var audioFrames = timeline.Audio.DurationFrames > 0 ? timeline.Audio.DurationFrames : totalFrames;

        var videoTrack = new XElement("track",
            clips.Select((clip, index) =>
                BuildVideoClipItem(clip, dimensions, index, timebase, timeline.Resolution, options)),
            new XElement("enabled", "TRUE"));

        var audioTrack = new XElement("track",
            BuildAudioClipItem(timeline, timebase, audioFrames),
            new XElement("enabled", "TRUE"));

        var sequence = new XElement("sequence",
            new XAttribute("id", "sequence-1"),
            new XAttribute("TL.SQAudioVisibleBase", "0"),
            new XAttribute("TL.SQVideoVisibleBase", "0"),
            new XAttribute("MZ.Sequence.PreviewFrameSizeHeight",
                timeline.Resolution.Height.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("MZ.Sequence.PreviewFrameSizeWidth",
                timeline.Resolution.Width.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("explodedTracks", "true"),
            new XElement("name", timeline.ProjectName),
            new XElement("duration", totalFrames.ToString(CultureInfo.InvariantCulture)),
            Rate(timebase),
            new XElement("timecode",
                Rate(timebase),
                new XElement("string", "00:00:00:00"),
                new XElement("frame", "0"),
                new XElement("displayformat", "NDF")),
            new XElement("media",
                new XElement("video",
                    new XElement("format",
                        new XElement("samplecharacteristics",
                            Rate(timebase),
                            new XElement("width", timeline.Resolution.Width.ToString(CultureInfo.InvariantCulture)),
                            new XElement("height", timeline.Resolution.Height.ToString(CultureInfo.InvariantCulture)),
                            new XElement("pixelaspectratio", "square"))),
                    videoTrack),
                new XElement("audio",
                    new XElement("samplecharacteristics",
                        new XElement("depth", "16"),
                        new XElement("samplerate", "48000")),
                    audioTrack)));

        var root = new XElement("xmeml", new XAttribute("version", "5"), sequence);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + root;
    }

    private static XElement BuildVideoClipItem(
        VideoClip clip,
        IReadOnlyDictionary<string, (int Width, int Height)> dimensions,
        int index,
        int timebase,
        Resolution resolution,
        PremiereExportOptions options)
    {
        var (sourceWidth, sourceHeight) = dimensions[clip.FilePath];
        var duration = clip.DurationFrames;
        var fileName = Path.GetFileName(clip.FilePath);
        var idSuffix = (index + 1).ToString(CultureInfo.InvariantCulture);

        var clipItem = new XElement("clipitem",
            new XAttribute("id", $"clipitem-{idSuffix}"),
            new XElement("masterclipid", $"masterclip-{idSuffix}"),
            new XElement("name", fileName),
            new XElement("enabled", "TRUE"),
            new XElement("duration", duration.ToString(CultureInfo.InvariantCulture)),
            Rate(timebase),
            new XElement("start", clip.StartFrame.ToString(CultureInfo.InvariantCulture)),
            new XElement("end", (clip.StartFrame + duration).ToString(CultureInfo.InvariantCulture)),
            new XElement("in", "0"),
            new XElement("out", duration.ToString(CultureInfo.InvariantCulture)),
            new XElement("file",
                new XAttribute("id", $"file-{idSuffix}"),
                new XElement("name", fileName),
                new XElement("pathurl", ToPathUrl(clip.FilePath)),
                Rate(timebase),
                new XElement("duration", duration.ToString(CultureInfo.InvariantCulture)),
                new XElement("media",
                    new XElement("video",
                        new XElement("samplecharacteristics",
                            Rate(timebase),
                            new XElement("width", sourceWidth.ToString(CultureInfo.InvariantCulture)),
                            new XElement("height", sourceHeight.ToString(CultureInfo.InvariantCulture)))))));

        if (options.IncludeMotionKeyframes)
        {
            clipItem.Add(BuildMotionEffect(clip, sourceWidth, sourceHeight, resolution, idSuffix));
        }

        return clipItem;
    }

    private static XElement BuildMotionEffect(
        VideoClip clip, int sourceWidth, int sourceHeight, Resolution resolution, string idSuffix)
    {
        var duration = clip.DurationFrames;
        var keyframeFrames = duration > 1 ? new[] { 0L, duration - 1 } : new[] { 0L };

        double ViewportWidth(long f) => Lerp(clip.StartViewport.Width, clip.EndViewport.Width, f, duration);
        double ViewportHeight(long f) => Lerp(clip.StartViewport.Height, clip.EndViewport.Height, f, duration);
        double ViewportCenterX(long f) =>
            Lerp(clip.StartViewport.X + clip.StartViewport.Width / 2.0,
                 clip.EndViewport.X + clip.EndViewport.Width / 2.0, f, duration);
        double ViewportCenterY(long f) =>
            Lerp(clip.StartViewport.Y + clip.StartViewport.Height / 2.0,
                 clip.EndViewport.Y + clip.EndViewport.Height / 2.0, f, duration);

        double ScaleAt(long f) => sourceWidth * 100.0 / ViewportWidth(f);
        double HorizAt(long f) =>
            resolution.Width / 2.0 +
            (sourceWidth / 2.0 - ViewportCenterX(f)) * (resolution.Width / ViewportWidth(f));
        double VertAt(long f) =>
            resolution.Height / 2.0 +
            (sourceHeight / 2.0 - ViewportCenterY(f)) * (resolution.Height / ViewportHeight(f));

        var scaleParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "scale"),
            new XElement("name", "Scale"),
            new XElement("value", F(ScaleAt(keyframeFrames[0]))),
            keyframeFrames.Select(f => new XElement("keyframe",
                new XElement("when", f.ToString(CultureInfo.InvariantCulture)),
                new XElement("value", F(ScaleAt(f))))));

        var centerParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "center"),
            new XElement("name", "Position"),
            CenterValue(HorizAt(keyframeFrames[0]), VertAt(keyframeFrames[0])),
            keyframeFrames.Select(f => new XElement("keyframe",
                new XElement("when", f.ToString(CultureInfo.InvariantCulture)),
                CenterValue(HorizAt(f), VertAt(f)))));

        var rotationParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "rotation"),
            new XElement("name", "Rotation"),
            new XElement("value", "0"));

        return new XElement("effect",
            new XAttribute("id", $"basic-motion-{idSuffix}"),
            new XElement("name", "Motion"),
            new XElement("effectid", "basic"),
            new XElement("effectcategory", "motion"),
            new XElement("effecttype", "transform"),
            new XElement("mediatype", "video"),
            scaleParameter,
            centerParameter,
            rotationParameter);
    }

    private static XElement BuildAudioClipItem(Timeline timeline, int timebase, long audioFrames)
    {
        var fileName = Path.GetFileName(timeline.Audio.FilePath);
        return new XElement("clipitem",
            new XAttribute("id", "clipitem-audio-1"),
            new XElement("name", fileName),
            new XElement("enabled", "TRUE"),
            new XElement("duration", audioFrames.ToString(CultureInfo.InvariantCulture)),
            Rate(timebase),
            new XElement("start", "0"),
            new XElement("end", audioFrames.ToString(CultureInfo.InvariantCulture)),
            new XElement("in", "0"),
            new XElement("out", audioFrames.ToString(CultureInfo.InvariantCulture)),
            new XElement("file",
                new XAttribute("id", "file-audio-1"),
                new XElement("name", fileName),
                new XElement("pathurl", ToPathUrl(timeline.Audio.FilePath)),
                Rate(timebase),
                new XElement("duration", audioFrames.ToString(CultureInfo.InvariantCulture)),
                new XElement("media",
                    new XElement("audio",
                        new XElement("samplecharacteristics",
                            new XElement("depth", "16"),
                            new XElement("samplerate", "48000")),
                        new XElement("channelcount", "2")))),
            new XElement("sourcetrack",
                new XElement("mediatype", "audio"),
                new XElement("trackindex", "1")));
    }

    private static XElement CenterValue(double horiz, double vert) =>
        new XElement("value",
            new XElement("horiz", F(horiz)),
            new XElement("vert", F(vert)));

    private static double Lerp(double start, double end, long frame, long duration) =>
        duration > 1 ? start + (end - start) * frame / (duration - 1) : start;

    private static XElement Rate(int timebase) =>
        new XElement("rate",
            new XElement("timebase", timebase.ToString(CultureInfo.InvariantCulture)),
            new XElement("ntsc", "FALSE"));

    private static string ToPathUrl(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var segments = fullPath.Replace('\\', '/').Split('/');
        var escaped = string.Join("/", segments.Select(s => Uri.EscapeDataString(s).Replace("%3A", ":")));
        return "file://localhost/" + escaped;
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
