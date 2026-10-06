using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Ffmpeg;

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
        LoadPlanning();
        LoadTiming();
        LoadMotion();
        LoadTransitions();
        LoadNaming();
        LoadOutput();
        LoadSceneInference();
        LoadRender();
        LoadSound();
    }

    private void LoadPlanning()
    {
        CmbPlanner.ItemsSource = new[] { "v1", "v2" };
        CmbPlanner.SelectedItem = string.Equals(Options.Planner, "v2", StringComparison.OrdinalIgnoreCase)
            ? "v2"
            : "v1";
    }

    private void LoadTiming()
    {
        TxtTimingMin.Text = F(Options.Timing.MinImageSeconds);
        TxtTimingPreferred.Text = F(Options.Timing.PreferredImageSeconds);
        TxtTimingMax.Text = F(Options.Timing.MaxImageSeconds);
        TxtTimingFloor.Text = F(Options.Timing.FloorImageSeconds);
    }

    private void LoadMotion()
    {
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
        UpdateMotionDefaultStatus();
    }

    private void UpdateMotionDefaultStatus()
    {
        TxtMotionDefaultStatus.Text = AppSettingsStore.Load().DefaultMotion is not null
            ? "Your default is saved for new projects"
            : "No personal default saved yet - new projects use the built-in Motion defaults.";
    }

    private void LoadTransitions()
    {
        ChkTransitionsEnabled.IsChecked = Options.Transitions.Enabled;
        CmbTransitionKind.ItemsSource = TransitionCatalog.Shortlist.Select(t => t.Name).ToList();
        CmbTransitionKind.SelectedItem = TransitionCatalog.NameOf(Options.Transitions.Kind);
        CmbSceneBoundaryKind.ItemsSource = TransitionCatalog.Shortlist.Select(t => t.Name).ToList();
        CmbSceneBoundaryKind.SelectedItem = TransitionCatalog.NameOf(Options.Transitions.SceneBoundaryKind);
        CmbTransitionAlignment.ItemsSource = TransitionAlignments.All.Select(a => a.Name).ToList();
        CmbTransitionAlignment.SelectedItem = TransitionAlignments.NameOf(Options.Transitions.Alignment);
        TxtTransitionDuration.Text = F(Options.Transitions.DurationSeconds);
        UpdateTransitionsDefaultStatus();
    }

    private void UpdateTransitionsDefaultStatus()
    {
        TxtTransitionsDefaultStatus.Text = AppSettingsStore.Load().DefaultTransitions is not null
            ? "Your default is saved for new projects"
            : "No personal default saved yet - new projects use the built-in Transitions defaults.";
    }

    private void LoadNaming()
    {
        ChkTypeCodes.IsChecked = Options.Naming.TypeCodesEnabled;
        ChkMotionCodes.IsChecked = Options.Naming.MotionCodesEnabled;
        TxtScenePrefix.Text = Options.Naming.ScenePrefix;
        TxtNumberPadding.Text = Options.Naming.NumberPadding.ToString(CultureInfo.InvariantCulture);
        TxtSeparator.Text = Options.Naming.Separator;
        TxtExtensions.Text = string.Join(", ", Options.Naming.ImageExtensions);
        UpdateNamingExample();
    }

    private void LoadOutput()
    {
        TxtOutputWidth.Text = Options.Output.Width.ToString(CultureInfo.InvariantCulture);
        TxtOutputHeight.Text = Options.Output.Height.ToString(CultureInfo.InvariantCulture);
        TxtOutputFps.Text = F(Options.Output.Fps);
        LoadResolutionPreset();
        UpdateResolutionDefaultStatus();
    }

    private void UpdateResolutionDefaultStatus()
    {
        var saved = AppSettingsStore.Load();
        TxtResolutionDefaultStatus.Text = saved.DefaultOutputWidth is int w && saved.DefaultOutputHeight is int h
            ? $"Your default: {w} × {h}"
            : "No personal default saved yet - new projects use 2560 × 1440.";
    }

    private void LoadSceneInference()
    {
        TxtSentenceGap.Text = F(Options.SceneInference.SentenceGapSeconds);
        TxtTerminalGap.Text = F(Options.SceneInference.TerminalPunctuationGapSeconds);
    }

    private void LoadSound()
    {
        CmbDefaultPop.ItemsSource = SoundCatalog.PopVariants
            .Select(v => new PopChoice(v.Id, v.Label))
            .ToList();
        CmbDefaultPop.SelectedValue = SoundCatalog.IsPopVariant(Options.Sound.DefaultPop)
            ? Options.Sound.DefaultPop
            : SoundCatalog.DefaultPopId;
        UpdatePopDefaultStatus();
    }

    private void UpdatePopDefaultStatus()
    {
        var saved = AppSettingsStore.Load().DefaultPop;
        TxtPopDefaultStatus.Text = SoundCatalog.IsPopVariant(saved)
            ? "Your default: " + SoundCatalog.PopVariants.First(v => string.Equals(v.Id, saved, StringComparison.OrdinalIgnoreCase)).Label
            : "No personal default saved yet - new projects use " + SoundCatalog.DefaultPopId + ".";
    }

    // A personal preference (this user, this machine): written straight to AppSettings,
    // independent of the dialog's Save/Cancel, and it only seeds a BRAND NEW project.
    private void BtnSavePopDefault_Click(object sender, RoutedEventArgs e)
    {
        if (CmbDefaultPop.SelectedValue is not string id)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        settings.DefaultPop = id;
        AppSettingsStore.Save(settings);
        UpdatePopDefaultStatus();
    }

    private sealed record PopChoice(string Id, string Label);

    private void BtnResetSound_Click(object sender, RoutedEventArgs e)
    {
        Options.Sound = new SoundOptions();
        LoadSound();
    }

    /// <summary>Renders the selected sound to a temp file with ffmpeg and plays it, so a choice can be auditioned.</summary>
    private async void BtnPlayPop_Click(object sender, RoutedEventArgs e)
    {
        if (CmbDefaultPop.SelectedValue is not string id)
        {
            return;
        }

        BtnPlayPop.IsEnabled = false;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "ImgToVideo-" + id + ".wav");
            var args = SoundEffectLibrary.BuildArguments(id, path);
            var runner = new ImgToVideo.Ffmpeg.FfmpegRunner(
                TxtFfmpegPath.Text.Length > 0 ? TxtFfmpegPath.Text : Options.Render.FfmpegPath);
            var result = await runner.RunAsync(args!);
            if (!result.Success || !File.Exists(path))
            {
                TxtPopStatus.Text = "Could not generate the sound - is ffmpeg available? (see Render & tools)";
                return;
            }

            using var player = new System.Media.SoundPlayer(path);
            player.Load();
            player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            TxtPopStatus.Text = "Could not play the sound: " + ex.Message;
        }
        finally
        {
            BtnPlayPop.IsEnabled = true;
        }
    }

    private void LoadRender()
    {
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

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var choice = MessageBox.Show(this,
            "Reset ALL settings to their defaults?\n\n" +
            "This only fills the dialog with defaults — click Save to apply them to the project.",
            "ImgToVideo", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (choice != MessageBoxResult.OK)
        {
            return;
        }

        Options = new ProjectOptions();
        LoadFromOptions();
    }

    private void BtnResetPlanning_Click(object sender, RoutedEventArgs e)
    {
        Options.Planner = new ProjectOptions().Planner;
        LoadPlanning();
    }

    private void BtnResetTiming_Click(object sender, RoutedEventArgs e)
    {
        Options.Timing = new TimingOptions();
        LoadTiming();
    }

    private void BtnResetMotion_Click(object sender, RoutedEventArgs e)
    {
        Options.Motion = new MotionOptions();
        LoadMotion();
    }

    private void BtnResetTransitions_Click(object sender, RoutedEventArgs e)
    {
        Options.Transitions = new TransitionOptions();
        LoadTransitions();
    }

    private void BtnResetNaming_Click(object sender, RoutedEventArgs e)
    {
        Options.Naming = new NamingOptions();
        LoadNaming();
    }

    private void BtnResetOutput_Click(object sender, RoutedEventArgs e)
    {
        Options.Output = new OutputOptions();
        LoadOutput();
    }

    private void BtnResetSceneInference_Click(object sender, RoutedEventArgs e)
    {
        Options.SceneInference = new SceneInferenceOptions();
        LoadSceneInference();
    }

    private void BtnResetRender_Click(object sender, RoutedEventArgs e)
    {
        Options.Render = new RenderOptions();
        LoadRender();
    }

    private void LoadResolutionPreset()
    {
        // 1376x768 is Google Flow's native master size, offered as a selectable
        // preset for projects sourced from Flow stills - it is NOT the default
        // (2560x1440 / "2K" stays the default; see docs/next-session.md 2026-09-28).
        var presets = new[] { (1920, 1080, "HD"), (2560, 1440, "2K"), (3840, 2160, "4K"), (1376, 768, "Flow native") };
        CmbResolution.ItemsSource = presets.Select(p => $"{p.Item1} × {p.Item2} ({p.Item3})").ToList();
        var match = presets.FirstOrDefault(p => p.Item1 == Options.Output.Width && p.Item2 == Options.Output.Height);
        CmbResolution.SelectedItem = match.Item3 is not null
            ? $"{match.Item1} × {match.Item2} ({match.Item3})"
            : $"{Options.Output.Width} × {Options.Output.Height} (custom)";
    }

    private void CmbResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbResolution.SelectedItem is not string text ||
            text.Contains("custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\s*×\s*(\d+)");
        if (match.Success)
        {
            TxtOutputWidth.Text = match.Groups[1].Value;
            TxtOutputHeight.Text = match.Groups[2].Value;
        }
    }

    // Reads the Motion section's fields exactly as BtnSave_Click's Options.Motion
    // would, but as its own method so the "Save as my default" button can build
    // the same MotionOptions without going through a full project Save.
    private MotionOptions BuildMotionFromFields(MotionOptions fallback) => new()
    {
        AutoMotionEnabled = ChkAutoMotion.IsChecked == true,
        MotionDurationMs = L(TxtMotionDuration.Text, fallback.MotionDurationMs),
        Easing = EasingModes.TryFromName(CmbEasing.SelectedItem as string ?? "", out var easing)
            ? easing
            : fallback.Easing,
        PushInStartPercent = D(TxtPushInStart.Text, fallback.PushInStartPercent),
        PushInEndPercent = D(TxtPushInEnd.Text, fallback.PushInEndPercent),
        ZoomOutStartPercent = D(TxtZoomOutStart.Text, fallback.ZoomOutStartPercent),
        ZoomOutEndPercent = D(TxtZoomOutEnd.Text, fallback.ZoomOutEndPercent),
        PanMaxTravelPercent = D(TxtPanTravel.Text, fallback.PanMaxTravelPercent),
        StaticEveryMinShots = I(TxtStaticMin.Text, fallback.StaticEveryMinShots),
        StaticEveryMaxShots = I(TxtStaticMax.Text, fallback.StaticEveryMaxShots),
    };

    private TransitionOptions BuildTransitionsFromFields(TransitionOptions fallback) => new()
    {
        Enabled = ChkTransitionsEnabled.IsChecked == true,
        Kind = TransitionCatalog.TryFromName(CmbTransitionKind.SelectedItem as string ?? "", out var kind)
            ? kind
            : fallback.Kind,
        SceneBoundaryKind = TransitionCatalog.TryFromName(
                CmbSceneBoundaryKind.SelectedItem as string ?? "", out var boundaryKind)
            ? boundaryKind
            : fallback.SceneBoundaryKind,
        Alignment = TransitionAlignments.TryFromName(
                CmbTransitionAlignment.SelectedItem as string ?? "", out var alignment)
            ? alignment
            : fallback.Alignment,
        DurationSeconds = D(TxtTransitionDuration.Text, fallback.DurationSeconds),
    };

    private void BtnSaveMotionDefault_Click(object sender, RoutedEventArgs e)
    {
        var saved = AppSettingsStore.Load();
        saved.DefaultMotion = BuildMotionFromFields(Options.Motion);
        AppSettingsStore.Save(saved);
        UpdateMotionDefaultStatus();
    }

    private void BtnSaveTransitionsDefault_Click(object sender, RoutedEventArgs e)
    {
        var saved = AppSettingsStore.Load();
        saved.DefaultTransitions = BuildTransitionsFromFields(Options.Transitions);
        AppSettingsStore.Save(saved);
        UpdateTransitionsDefaultStatus();
    }

    private void BtnSaveResolutionDefault_Click(object sender, RoutedEventArgs e)
    {
        var width = I(TxtOutputWidth.Text, Options.Output.Width);
        var height = I(TxtOutputHeight.Text, Options.Output.Height);
        if (width <= 0 || height <= 0)
        {
            MessageBox.Show(this, "Enter a valid Width and Height before saving a default.",
                "ImgToVideo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // A personal preference (this user, this machine), not a project
        // setting - written straight to AppSettings rather than staged into
        // Options/imgtovideo.json, so it takes effect immediately and
        // independently of whether the user clicks Save or Cancel below.
        var saved = AppSettingsStore.Load();
        saved.DefaultOutputWidth = width;
        saved.DefaultOutputHeight = height;
        AppSettingsStore.Save(saved);
        UpdateResolutionDefaultStatus();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var next = new ProjectOptions
        {
            Planner = CmbPlanner.SelectedItem as string ?? Options.Planner,
            Sound = new SoundOptions
            {
                DefaultPop = CmbDefaultPop.SelectedValue as string ?? Options.Sound.DefaultPop,
            },

            Timing = new TimingOptions
            {
                MinImageSeconds = D(TxtTimingMin.Text, Options.Timing.MinImageSeconds),
                PreferredImageSeconds = D(TxtTimingPreferred.Text, Options.Timing.PreferredImageSeconds),
                MaxImageSeconds = D(TxtTimingMax.Text, Options.Timing.MaxImageSeconds),
                FloorImageSeconds = D(TxtTimingFloor.Text, Options.Timing.FloorImageSeconds),
            },
            Motion = BuildMotionFromFields(Options.Motion),
            Transitions = BuildTransitionsFromFields(Options.Transitions),
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
                PreviewBframes = Options.Render.PreviewBframes,
                FinalPreset = TxtFinalPreset.Text.Length > 0 ? TxtFinalPreset.Text : Options.Render.FinalPreset,
                FinalCrf = I(TxtFinalCrf.Text, Options.Render.FinalCrf),
                FinalBframes = Options.Render.FinalBframes,
                SupersampleTargetWidth = Options.Render.SupersampleTargetWidth,
                Encoder = TxtEncoder.Text.Length > 0 ? TxtEncoder.Text : Options.Render.Encoder,
                FfmpegPath = TxtFfmpegPath.Text.Length > 0 ? TxtFfmpegPath.Text : Options.Render.FfmpegPath,
                FfprobePath = TxtFfprobePath.Text.Length > 0 ? TxtFfprobePath.Text : Options.Render.FfprobePath,
            },
        };

        next.KeepSectionsNotEditedInSettings(Options);

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
