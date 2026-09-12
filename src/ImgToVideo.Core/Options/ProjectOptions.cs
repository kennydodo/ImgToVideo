using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class ProjectOptions
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public OutputOptions Output { get; set; } = new();
    public TimingOptions Timing { get; set; } = new();
    public MotionOptions Motion { get; set; } = new();
    public TransitionOptions Transitions { get; set; } = new();
    public NamingOptions Naming { get; set; } = new();
    public RenderOptions Render { get; set; } = new();
    public SceneInferenceOptions SceneInference { get; set; } = new();

    /// <summary>"v1" = scene-inference planner, "v2" = visual_manifest.json shots.
    /// v2 is the default; projects without a manifest still plan via v1 inference.</summary>
    public string Planner { get; set; } = "v2";

    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Output.Width <= 0 || Output.Height <= 0 || Output.Width % 2 != 0 || Output.Height % 2 != 0)
        {
            errors.Add("Output width and height must be positive even numbers (H.264 requirement).");
        }

        if (Output.Fps <= 0)
        {
            errors.Add("Output fps must be positive.");
        }

        var t = Timing;
        if (t.FloorImageSeconds <= 0 || t.MinImageSeconds <= 0 || t.PreferredImageSeconds <= 0 || t.MaxImageSeconds <= 0)
        {
            errors.Add("Timing durations must be positive.");
        }
        else if (!(t.FloorImageSeconds <= t.MinImageSeconds &&
                   t.MinImageSeconds <= t.PreferredImageSeconds &&
                   t.PreferredImageSeconds <= t.MaxImageSeconds))
        {
            errors.Add("Timing must satisfy floor <= min <= preferred <= max.");
        }

        var m = Motion;
        if (m.PushInStartPercent >= m.PushInEndPercent)
        {
            errors.Add("Push-in start percent must be below push-in end percent.");
        }

        if (m.ZoomOutStartPercent <= m.ZoomOutEndPercent)
        {
            errors.Add("Zoom-out start percent must be above zoom-out end percent.");
        }

        if (m.PanMaxTravelPercent < 0 || m.PanMaxTravelPercent > 25)
        {
            errors.Add("Pan max travel percent must be between 0 and 25.");
        }

        if (m.StaticEveryMinShots < 1 || m.StaticEveryMinShots > m.StaticEveryMaxShots)
        {
            errors.Add("Static cadence must satisfy 1 <= min shots <= max shots.");
        }

        var tr = Transitions;
        if (tr.Enabled && tr.DurationSeconds <= 0)
        {
            errors.Add("Transition duration must be positive when transitions are enabled.");
        }

        var n = Naming;
        if (string.IsNullOrWhiteSpace(n.ScenePrefix))
        {
            errors.Add("Scene prefix must not be empty.");
        }

        if (string.IsNullOrEmpty(n.Separator))
        {
            errors.Add("Separator must not be empty.");
        }

        if (n.NumberPadding < 1 || n.NumberPadding > 4)
        {
            errors.Add("Number padding must be between 1 and 4.");
        }

        if (n.ImageExtensions.Count == 0)
        {
            errors.Add("At least one image extension must be listed.");
        }

        var si = SceneInference;
        if (si.SentenceGapSeconds <= 0 || si.TerminalPunctuationGapSeconds <= 0)
        {
            errors.Add("Scene inference gaps must be positive.");
        }
        else if (si.TerminalPunctuationGapSeconds > si.SentenceGapSeconds)
        {
            errors.Add("Terminal punctuation gap must not exceed the sentence gap.");
        }

        var r = Render;
        if (r.PreviewWidth <= 0 || r.PreviewHeight <= 0)
        {
            errors.Add("Preview width and height must be positive.");
        }

        if (r.PreviewCrf < 0 || r.PreviewCrf > 51)
        {
            errors.Add("Preview CRF must be between 0 and 51.");
        }

        if (r.FinalCrf < 0 || r.FinalCrf > 51)
        {
            errors.Add("Final CRF must be between 0 and 51.");
        }

        if (Planner is not ("v1" or "v2"))
        {
            errors.Add("Planner must be \"v1\" or \"v2\".");
        }

        if (r.Encoder is not ("auto" or "libx264" or "cpu" or "h264_nvenc" or "nvenc" or
            "h264_amf" or "amf" or "h264_qsv" or "qsv"))
        {
            errors.Add("Encoder must be auto, libx264, h264_nvenc, h264_amf or h264_qsv.");
        }

        return errors;
    }
}
