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

        // V1 carries every clip at its timeline position.
        //
        // Crossfades (Crossfade/Dissolve): a superimposed copy of the incoming
        // clip on V2, trimmed to exactly its fade-in window — overlays never
        // overlap each other on the track, so Premiere cannot trim one away
        // (that collapsed later crossfades into cuts).
        //
        // Dips (FadeBlack/FadeWhite): sequential on V1, no overlays — the
        // outgoing clip's tail fades to black and the incoming clip's head
        // fades in from it. FadeWhite shares this path (dip through dark);
        // a true white flash would need a matte layer.
        var baseItems = new List<XElement>();
        var overlayItems = new List<XElement>();

        for (var index = 0; index < clips.Count; index++)
        {
            var clip = clips[index];
            var duration = clip.DurationFrames;

            // Opacity keyframes on the V1 base clip: a head fade when this clip
            // arrives through a dip, a tail fade when the next join leaves
            // through one. Crossfades never touch the base clip's opacity —
            // their overlay handles the blend.
            var opacity = new List<(long When, double Value)>();
            if (index > 0
                && clip.Transition is { } incoming
                && incoming.DurationFrames is { } inFrames && inFrames >= 2
                && incoming.Kind is TransitionKind.FadeBlack or TransitionKind.FadeWhite)
            {
                opacity.Add((0, 0.0));
                opacity.Add((Math.Min(inFrames, duration), 100.0));
            }

            if (index + 1 < clips.Count
                && clips[index + 1].Transition is { } outgoing
                && outgoing.DurationFrames is { } outFrames && outFrames >= 2
                && outgoing.Kind is TransitionKind.FadeBlack or TransitionKind.FadeWhite)
            {
                var tailStart = Math.Max(duration - outFrames, opacity.Count > 0 ? opacity[^1].When : 0);
                if (opacity.Count == 0 || opacity[^1].When < tailStart)
                {
                    opacity.Add((tailStart, 100.0));
                }

                opacity.Add((duration, 0.0));
            }

            baseItems.Add(BuildVideoClipItem(
                clip, dimensions, index, timebase, timeline.Resolution, options,
                startOffset: 0, duration: duration, frozenMotion: false,
                opacityRamp: opacity.Count > 0 ? opacity.ToArray() : null));

            if (index > 0
                && clip.Transition is { } fadeIn
                && fadeIn.DurationFrames is { } fadeFrames && fadeFrames >= 2
                && fadeIn.Kind is TransitionKind.Crossfade or TransitionKind.Dissolve)
            {
                overlayItems.Add(BuildVideoClipItem(
                    clip, dimensions, index, timebase, timeline.Resolution, options,
                    startOffset: -fadeFrames, duration: fadeFrames, frozenMotion: true,
                    opacityRamp: new[] { (0L, 0.0), (fadeFrames, 100.0) }));
            }
        }

        var videoTrack = new XElement("track",
            baseItems,
            new XElement("enabled", "TRUE"));

        var videoMedia = new XElement("video",
            new XElement("format",
                new XElement("samplecharacteristics",
                    Rate(timebase),
                    new XElement("width", timeline.Resolution.Width.ToString(CultureInfo.InvariantCulture)),
                    new XElement("height", timeline.Resolution.Height.ToString(CultureInfo.InvariantCulture)),
                    new XElement("pixelaspectratio", "square"))),
            videoTrack);

        if (overlayItems.Count > 0)
        {
            videoMedia.Add(new XElement("track",
                overlayItems,
                new XElement("enabled", "TRUE")));
        }

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
                videoMedia,
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
        PremiereExportOptions options,
        long startOffset,
        long duration,
        bool frozenMotion,
        (long When, double Value)[]? opacityRamp)
    {
        var (sourceWidth, sourceHeight) = dimensions[clip.FilePath];
        var fileName = Path.GetFileName(clip.FilePath);
        var idSuffix = (index + 1).ToString(CultureInfo.InvariantCulture) +
                       (frozenMotion ? "-x" : "");
        var start = clip.StartFrame + startOffset;

        var clipItem = new XElement("clipitem",
            new XAttribute("id", $"clipitem-{idSuffix}"),
            new XElement("masterclipid", $"masterclip-{idSuffix}"),
            new XElement("name", fileName),
            new XElement("enabled", "TRUE"),
            new XElement("duration", duration.ToString(CultureInfo.InvariantCulture)),
            Rate(timebase),
            new XElement("start", start.ToString(CultureInfo.InvariantCulture)),
            new XElement("end", (start + duration).ToString(CultureInfo.InvariantCulture)),
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

        // Motion keyframes live in the Basic Motion effect; opacity ramps live in
        // their own filter element, serialized exactly as Premiere itself exports
        // them (verified against a reference export): parameterid "opacity" — the
        // FCP7 spec's "level" is Premiere's AUDIO levels parameter — inside an
        // opacity effect with effecttype "motion". A custom parameter inside the
        // basic-motion effect was silently dropped on import.
        if (options.IncludeMotionKeyframes)
        {
            clipItem.Add(new XElement("filter", BuildMotionFilter(
                clip, sourceWidth, sourceHeight, resolution, idSuffix,
                frozenMotion ? 1 : duration, timebase, frozenMotion)));
        }

        if (opacityRamp is { } ramp)
        {
            clipItem.Add(BuildOpacityFilter(ramp));
        }

        return clipItem;
    }

    private static XElement BuildMotionFilter(
        VideoClip clip,
        int sourceWidth,
        int sourceHeight,
        Resolution resolution,
        string idSuffix,
        long duration,
        int timebase,
        bool frozenMotion = false)
    {
        double Progress(long f) => duration > 1
            ? Eased(clip.Easing, f / (double)(duration - 1))
            : 0.0;

        double ViewportWidth(long f) =>
            clip.StartViewport.Width + (clip.EndViewport.Width - clip.StartViewport.Width) * Progress(f);
        double ViewportHeight(long f) =>
            clip.StartViewport.Height + (clip.EndViewport.Height - clip.StartViewport.Height) * Progress(f);
        double ViewportCenterX(long f) =>
            clip.StartViewport.X + clip.StartViewport.Width / 2.0 +
            (clip.EndViewport.X + clip.EndViewport.Width / 2.0 -
             (clip.StartViewport.X + clip.StartViewport.Width / 2.0)) * Progress(f);
        double ViewportCenterY(long f) =>
            clip.StartViewport.Y + clip.StartViewport.Height / 2.0 +
            (clip.EndViewport.Y + clip.EndViewport.Height / 2.0 -
             (clip.StartViewport.Y + clip.StartViewport.Height / 2.0)) * Progress(f);

        // Premiere Motion scale is relative to the image's NATIVE pixel size.
        // The viewport must fill the frame: frameWidth / viewportWidth.
        // (sourceWidth*100/viewportWidth was wrong — it over-zoomed images
        // larger than the sequence frame by ~20%.)
        double ScaleAt(long f) => resolution.Width * 100.0 / ViewportWidth(f);

        // Premiere's Motion center is relative to the FRAME CENTER (0,0),
        // not absolute sequence pixels — absolute values push the image
        // off-screen and render black frames.
        double HorizAt(long f) =>
            (sourceWidth / 2.0 - ViewportCenterX(f)) * (resolution.Width / ViewportWidth(f));
        double VertAt(long f) =>
            (sourceHeight / 2.0 - ViewportCenterY(f)) * (resolution.Height / ViewportHeight(f));

        // Sample the eased motion every ~half second (min 2, max 13 keyframes)
        // so Premiere reproduces the easing instead of a linear slide. Overlay
        // copies frozen on their first framing emit a single start keyframe —
        // V1 animates the real motion once the cut lands.
        var sampleCount = frozenMotion
            ? 1
            : Math.Clamp((int)Math.Ceiling(duration / (timebase / 2.0)), 2, 13);
        var samples = new List<long>();
        for (var i = 0; i < sampleCount; i++)
        {
            samples.Add(duration > 1 && !frozenMotion
                ? (long)Math.Round(i * (double)(duration - 1) / (sampleCount - 1))
                : 0);
        }

        var scaleParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "scale"),
            new XElement("name", "Scale"),
            new XElement("value", F(ScaleAt(samples[0]))),
            samples.Select(f => new XElement("keyframe",
                new XElement("when", f.ToString(CultureInfo.InvariantCulture)),
                new XElement("value", F(ScaleAt(f))))));

        var centerParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "position"),
            new XElement("name", "Position"),
            CenterValue(HorizAt(samples[0]), VertAt(samples[0])),
            samples.Select(f => new XElement("keyframe",
                new XElement("when", f.ToString(CultureInfo.InvariantCulture)),
                CenterValue(HorizAt(f), VertAt(f)))));

        var rotationParameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "rotation"),
            new XElement("name", "Rotation"),
            new XElement("value", "0"));

        var effect = new XElement("effect",
            new XAttribute("id", $"basic-motion-{idSuffix}"),
            new XElement("name", "Motion"),
            new XElement("effectid", "basic"),
            new XElement("effectcategory", "motion"),
            new XElement("effecttype", "transform"),
            new XElement("mediatype", "video"),
            scaleParameter,
            centerParameter,
            rotationParameter);

        return effect;
    }

    // Mirrors Premiere's own FCP XML serialization of clip opacity: the opacity
    // effect lives in its own filter, effecttype "motion", with a single
    // "opacity" parameter holding the keyframes.
    private static XElement BuildOpacityFilter((long When, double Value)[] keyframes)
    {
        var parameter = new XElement("parameter",
            new XAttribute("authoringApp", "PremierePro"),
            new XElement("parameterid", "opacity"),
            new XElement("name", "opacity"),
            new XElement("valuemin", "0"),
            new XElement("valuemax", "100"),
            new XElement("value", F(keyframes[0].Value)));
        foreach (var (when, value) in keyframes)
        {
            parameter.Add(new XElement("keyframe",
                new XElement("when", when.ToString(CultureInfo.InvariantCulture)),
                new XElement("value", F(value))));
        }

        return new XElement("filter",
            new XElement("effect",
                new XElement("name", "Opacity"),
                new XElement("effectid", "opacity"),
                new XElement("effectcategory", "motion"),
                new XElement("effecttype", "motion"),
                new XElement("mediatype", "video"),
                new XElement("pproBypass", "false"),
                parameter));
    }

    private static double Eased(EasingMode easing, double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return easing switch
        {
            EasingMode.EaseIn => progress * progress,
            EasingMode.EaseOut => 1 - (1 - progress) * (1 - progress),
            EasingMode.EaseInOut => progress * progress * progress * (progress * (progress * 6 - 15) + 10),
            _ => progress,
        };
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
