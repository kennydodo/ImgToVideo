using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.App;

public partial class SettingsWindow : Window
{
    public ProjectOptions Options { get; private set; }

    private readonly string _projectFolder;

    public SettingsWindow(ProjectOptions options, string projectFolder)
    {
        InitializeComponent();
        Options = options;
        _projectFolder = projectFolder;
        LoadFromOptions();

        TxtScenePrefix.TextChanged += (_, _) => UpdateNamingExample();
        TxtNumberPadding.TextChanged += (_, _) => UpdateNamingExample();
        TxtSeparator.TextChanged += (_, _) => UpdateNamingExample();
        ChkTypeCodes.Checked += (_, _) => UpdateNamingExample();
        ChkTypeCodes.Unchecked += (_, _) => UpdateNamingExample();
        ChkMotionCodes.Checked += (_, _) => UpdateNamingExample();
        ChkMotionCodes.Unchecked += (_, _) => UpdateNamingExample();

        UpdateNamingExample();
    }

    private void LoadFromOptions()
    {
        CmbPlanner.ItemsSource = new[] { "v1", "v2" };
        CmbPlanner.SelectedItem = string.Equals(Options.Planner, "v2", StringComparison.OrdinalIgnoreCase)
            ? "v2"
            : "v1";

        TxtTimingMin.Text = F(Options.Timing.MinImageSeconds);
        TxtTimingPreferred.Text = F(Options.Timing.PreferredImageSeconds);
        TxtTimingMax.Text = F(Options.Timing.MaxImageSeconds);
        TxtTimingFloor.Text = F(Options.Timing.FloorImageSeconds);

        ChkAutoMotion.IsChecked = Options.Motion.AutoMotionEnabled;
        TxtMotionDuration.Text = Options.Motion.MotionDurationMs.ToString(CultureInfo.InvariantCulture);
        CmbEasing.ItemsSource = EasingModes.All.Select(e => e.Name).ToList();
        CmbEasing.SelectedItem = EasingModes.NameOf(Options.Motion.Easing);
        TxtPushInStart.Text = F(Options.Motion.PushInStartPercent);
        TxtPushInEnd.Text = F(Options.Motion.PushInEndPercent);
        TxtZoomOutStart.Text = F(Options.Motion.ZoomOutStartPercent);
        TxtZoomOutEnd.Text = F(Options.Motion.ZoomOutEndPercent);
        TxtPanTravel.Text = F(Options.Motion.PanMaxTravelPercent);
        TxtStaticMin.Text = Options.Motion.StaticEveryMinShots.ToString(CultureInfo.InvariantCulture);
        TxtStaticMax.Text = Options.Motion.StaticEveryMaxShots.ToString(CultureInfo.InvariantCulture);

        ChkTransitionsEnabled.IsChecked = Options.Transitions.Enabled;
        CmbTransitionKind.ItemsSource = TransitionCatalog.Shortlist.Select(t => t.Name).ToList();
        CmbTransitionKind.SelectedItem = TransitionCatalog.NameOf(Options.Transitions.Kind);
        CmbSceneBoundaryKind.ItemsSource = TransitionCatalog.Shortlist.Select(t => t.Name).ToList();
        CmbSceneBoundaryKind.SelectedItem = TransitionCatalog.NameOf(Options.Transitions.SceneBoundaryKind);
        CmbTransitionAlignment.ItemsSource = TransitionAlignments.All.Select(a => a.Name).ToList();
        CmbTransitionAlignment.SelectedItem = TransitionAlignments.NameOf(Options.Transitions.Alignment);
        TxtTransitionDuration.Text = F(Options.Transitions.DurationSeconds);

        ChkTypeCodes.IsChecked = Options.Naming.TypeCodesEnabled;
        ChkMotionCodes.IsChecked = Options.Naming.MotionCodesEnabled;
        TxtScenePrefix.Text = Options.Naming.ScenePrefix;
        TxtNumberPadding.Text = Options.Naming.NumberPadding.ToString(CultureInfo.InvariantCulture);
        TxtSeparator.Text = Options.Naming.Separator;
        TxtExtensions.Text = string.Join(", ", Options.Naming.ImageExtensions);

        TxtOutputWidth.Text = Options.Output.Width.ToString(CultureInfo.InvariantCulture);
        TxtOutputHeight.Text = Options.Output.Height.ToString(CultureInfo.InvariantCulture);
        TxtOutputFps.Text = F(Options.Output.Fps);

        TxtSentenceGap.Text = F(Options.SceneInference.SentenceGapSeconds);
        TxtTerminalGap.Text = F(Options.SceneInference.TerminalPunctuationGapSeconds);

        TxtPreviewWidth.Text = Options.Render.PreviewWidth.ToString(CultureInfo.InvariantCulture);
        TxtPreviewHeight.Text = Options.Render.PreviewHeight.ToString(CultureInfo.InvariantCulture);
        TxtPreviewPreset.Text = Options.Render.PreviewPreset;
        TxtPreviewCrf.Text = Options.Render.PreviewCrf.ToString(CultureInfo.InvariantCulture);
        TxtFinalPreset.Text = Options.Render.FinalPreset;
        TxtFinalCrf.Text = Options.Render.FinalCrf.ToString(CultureInfo.InvariantCulture);
        TxtEncoder.Text = Options.Render.Encoder;
        TxtFfmpegPath.Text = Options.Render.FfmpegPath;
        TxtFfprobePath.Text = Options.Render.FfprobePath;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var next = new ProjectOptions
        {
            Planner = CmbPlanner.SelectedItem as string ?? Options.Planner,

            Timing = new TimingOptions
            {
                MinImageSeconds = D(TxtTimingMin.Text, Options.Timing.MinImageSeconds),
                PreferredImageSeconds = D(TxtTimingPreferred.Text, Options.Timing.PreferredImageSeconds),
                MaxImageSeconds = D(TxtTimingMax.Text, Options.Timing.MaxImageSeconds),
                FloorImageSeconds = D(TxtTimingFloor.Text, Options.Timing.FloorImageSeconds),
            },
            Motion = new MotionOptions
            {
                AutoMotionEnabled = ChkAutoMotion.IsChecked == true,
                MotionDurationMs = L(TxtMotionDuration.Text, Options.Motion.MotionDurationMs),
                Easing = EasingModes.TryFromName(CmbEasing.SelectedItem as string ?? "", out var easing)
                    ? easing
                    : Options.Motion.Easing,
                PushInStartPercent = D(TxtPushInStart.Text, Options.Motion.PushInStartPercent),
                PushInEndPercent = D(TxtPushInEnd.Text, Options.Motion.PushInEndPercent),
                ZoomOutStartPercent = D(TxtZoomOutStart.Text, Options.Motion.ZoomOutStartPercent),
                ZoomOutEndPercent = D(TxtZoomOutEnd.Text, Options.Motion.ZoomOutEndPercent),
                PanMaxTravelPercent = D(TxtPanTravel.Text, Options.Motion.PanMaxTravelPercent),
                StaticEveryMinShots = I(TxtStaticMin.Text, Options.Motion.StaticEveryMinShots),
                StaticEveryMaxShots = I(TxtStaticMax.Text, Options.Motion.StaticEveryMaxShots),
            },
            Transitions = new TransitionOptions
            {
                Enabled = ChkTransitionsEnabled.IsChecked == true,
                Kind = TransitionCatalog.TryFromName(CmbTransitionKind.SelectedItem as string ?? "", out var kind)
                    ? kind
                    : Options.Transitions.Kind,
                SceneBoundaryKind = TransitionCatalog.TryFromName(
                        CmbSceneBoundaryKind.SelectedItem as string ?? "", out var boundaryKind)
                    ? boundaryKind
                    : Options.Transitions.SceneBoundaryKind,
                Alignment = TransitionAlignments.TryFromName(
                        CmbTransitionAlignment.SelectedItem as string ?? "", out var alignment)
                    ? alignment
                    : Options.Transitions.Alignment,
                DurationSeconds = D(TxtTransitionDuration.Text, Options.Transitions.DurationSeconds),
            },
            Naming = new NamingOptions
            {
                MotionCodesEnabled = ChkMotionCodes.IsChecked == true,
                TypeCodesEnabled = ChkTypeCodes.IsChecked == true,
                ScenePrefix = TxtScenePrefix.Text.Length > 0 ? TxtScenePrefix.Text : Options.Naming.ScenePrefix,
                NumberPadding = I(TxtNumberPadding.Text, Options.Naming.NumberPadding),
                Separator = TxtSeparator.Text.Length > 0 ? TxtSeparator.Text : Options.Naming.Separator,
                ImageExtensions = TxtExtensions.Text
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => x.StartsWith('.') ? x : "." + x)
                    .ToList(),
            },
            Output = new OutputOptions
            {
                Width = I(TxtOutputWidth.Text, Options.Output.Width),
                Height = I(TxtOutputHeight.Text, Options.Output.Height),
                Fps = D(TxtOutputFps.Text, Options.Output.Fps),
            },
            SceneInference = new SceneInferenceOptions
            {
                SentenceGapSeconds = D(TxtSentenceGap.Text, Options.SceneInference.SentenceGapSeconds),
                TerminalPunctuationGapSeconds = D(TxtTerminalGap.Text, Options.SceneInference.TerminalPunctuationGapSeconds),
            },
            Render = new RenderOptions
            {
                PreviewWidth = I(TxtPreviewWidth.Text, Options.Render.PreviewWidth),
                PreviewHeight = I(TxtPreviewHeight.Text, Options.Render.PreviewHeight),
                PreviewPreset = TxtPreviewPreset.Text.Length > 0 ? TxtPreviewPreset.Text : Options.Render.PreviewPreset,
                PreviewCrf = I(TxtPreviewCrf.Text, Options.Render.PreviewCrf),
                FinalPreset = TxtFinalPreset.Text.Length > 0 ? TxtFinalPreset.Text : Options.Render.FinalPreset,
                FinalCrf = I(TxtFinalCrf.Text, Options.Render.FinalCrf),
                Encoder = TxtEncoder.Text.Length > 0 ? TxtEncoder.Text : Options.Render.Encoder,
                FfmpegPath = TxtFfmpegPath.Text.Length > 0 ? TxtFfmpegPath.Text : Options.Render.FfmpegPath,
                FfprobePath = TxtFfprobePath.Text.Length > 0 ? TxtFfprobePath.Text : Options.Render.FfprobePath,
            },
        };

        var errors = next.Validate();
        if (errors.Count > 0)
        {
            MessageBox.Show(this,
                "Settings are invalid:\n" + string.Join("\n", errors),
                "ImgToVideo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Options = next;
        if (_projectFolder.Length > 0)
        {
            try
            {
                OptionsJson.Save(Options, Path.Combine(_projectFolder, "imgtovideo.json"));
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Settings applied but not saved to disk: " + ex.Message,
                    "ImgToVideo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        DialogResult = true;
    }

    private void UpdateNamingExample()
    {
        var naming = new NamingOptions
        {
            ScenePrefix = TxtScenePrefix.Text.Length > 0 ? TxtScenePrefix.Text : "S",
            NumberPadding = I(TxtNumberPadding.Text, Options.Naming.NumberPadding),
            Separator = TxtSeparator.Text.Length > 0 ? TxtSeparator.Text : "_",
            MotionCodesEnabled = ChkMotionCodes.IsChecked == true,
            TypeCodesEnabled = ChkTypeCodes.IsChecked == true,
        };
        var parser = new ImgToVideo.Core.Parsing.ImageFilenameParser(naming);
        var example = parser.FormatExample(
            type: naming.TypeCodesEnabled ? "SCN" : null,
            code: naming.MotionCodesEnabled ? "PR" : null);
        TxtNamingExample.Text = "Example: " + example;
        TxtNamingExample.Foreground =
            (System.Windows.Media.SolidColorBrush)FindResource("BrushTextTertiary");
    }

    private static double D(string text, double fallback) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static int I(string text, int fallback) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static long L(string text, long fallback) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : fallback;

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
