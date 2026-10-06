using System.Globalization;
using System.IO;
using System.Windows;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Parsing;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Reporting;
using ImgToVideo.Core.Serialization;
using ImgToVideo.Ffmpeg;
using ImgToVideo.Premiere;
using Microsoft.Win32;

namespace ImgToVideo.App;

public partial class MainWindow : Window
{
    private sealed record DiagnosticsRow(string Text, System.Windows.Media.SolidColorBrush TextBrush, System.Windows.Media.SolidColorBrush TintBrush, bool IsHeader = false);

    private enum StatusKind { Neutral, Success, Error }

    private ProjectOptions _options = new();
    private ProjectInventory? _inventory;
    private PlanningResult? _planned;
    private CancellationTokenSource? _renderCts;
    private string _projectFolder = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        var lastFolder = AppSettingsStore.Load().LastProjectFolder;
        if (lastFolder.Length > 0)
        {
            TxtFolder.Text = lastFolder;
        }
    }

    private void TxtFolder_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _projectFolder = TxtFolder.Text.Trim();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select project folder",
        };
        if (Directory.Exists(_projectFolder))
        {
            dialog.InitialDirectory = _projectFolder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            TxtFolder.Text = dialog.FolderName;
        }
    }

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (!IsProjectFolderReady())
        {
            return;
        }

        try
        {
            SetBusy(true, "Analyzing…");

            var optionsPath = Path.Combine(_projectFolder, "imgtovideo.json");
            if (!File.Exists(optionsPath))
            {
                // First analyze in this folder: write the default options so there
                // is a file to edit (planner, fps, …) without hand-creating JSON.
                // If the user has saved personal defaults (Settings -> Output /
                // Motion / Transitions -> "Save as my default"), seed the new
                // project with those instead of ProjectOptions' own built-in
                // values - a project that already has its own imgtovideo.json is
                // never touched by this, only a brand new one.
                var seeded = new ProjectOptions();
                var savedDefault = AppSettingsStore.Load();
                if (savedDefault.DefaultOutputWidth is int dw && savedDefault.DefaultOutputHeight is int dh)
                {
                    seeded.Output.Width = dw;
                    seeded.Output.Height = dh;
                }
                if (savedDefault.DefaultMotion is not null)
                {
                    seeded.Motion = savedDefault.DefaultMotion;
                }
                if (savedDefault.DefaultTransitions is not null)
                {
                    seeded.Transitions = savedDefault.DefaultTransitions;
                }
                seeded.Sound.ApplyPersonalDefault(savedDefault.DefaultPop);

                try
                {
                    OptionsJson.Save(seeded, optionsPath);
                }
                catch (IOException)
                {
                    // Non-fatal — defaults still apply for this run.
                }
            }

            _options = OptionsJson.LoadOrDefault(optionsPath);
            SyncOptionControls();
            _planned = null;

            _inventory = ProjectLoader.Load(_projectFolder, _options);
            UpdateStats();
            UpdateIssues(_inventory.Issues);

            ShowStatus(ValidationIssue.HasErrors(_inventory.Issues)
                ? "Analysis found blocking errors — see diagnostics."
                : $"Analysis complete: {_inventory.AllImages.Count} images in {_inventory.SceneGroups.Count} scenes.",
                ValidationIssue.HasErrors(_inventory.Issues) ? StatusKind.Error : StatusKind.Success);

            await ProbeAudioDurationAsync();
        }
        catch (Exception ex)
        {
            ShowStatus("Analyze failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void BtnScenes_Click(object sender, RoutedEventArgs e)
    {
        if (_inventory is null || !IsProjectFolderReady())
        {
            return;
        }

        try
        {
            SetBusy(true, "Preparing scene editor…");
            if (!await PlanIfNeededAsync())
            {
                return;
            }

            var editor = new SceneEditorWindow(
                _projectFolder, _planned!.Timeline!, _inventory, _options) { Owner = this };
            editor.ShowDialog();

            if (editor.Saved)
            {
                // Re-read overrides.json (and any newly generated images) so the next
                // BUILD PREVIEW and the reopened editor see the saved state instead of
                // the stale in-memory copy from the last ANALYZE.
                _inventory = ProjectLoader.Load(_projectFolder, _options);
                UpdateIssues(_inventory.Issues);
                UpdateStats();

                _planned = null;
                BtnBuild.IsEnabled = true;
                BtnExport.IsEnabled = true;
                ShowStatus("Overrides saved — BUILD PREVIEW to apply them.", StatusKind.Success);
            }
        }
        catch (Exception ex)
        {
            ShowStatus("Scene editor failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void BtnBuild_Click(object sender, RoutedEventArgs e)
    {
        if (_inventory is null || !IsProjectFolderReady())
        {
            return;
        }

        try
        {
            SetBusy(true, "Building…");
            if (!await PlanIfNeededAsync())
            {
                return;
            }

            var outDir = Path.Combine(_projectFolder, "out");
            var renderDirectory = Path.Combine(outDir, "render");
            var previewPath = Path.Combine(outDir, "preview.mp4");

            var plan = PreviewRenderPlanFactory.Build(
                _planned!.Timeline!, _inventory.AllImages, _options, renderDirectory, previewPath);

            _renderCts = new CancellationTokenSource();
            var progress = new Progress<double>(p => PbRender.Value = p * 100);
            var service = new PreviewRenderService(
                new FfmpegRunner(PreviewRenderPlanFactory.ResolveEffectiveFfmpegPath(_options.Render)));
            var result = await service.RenderAsync(
                plan, maxParallelism: 2, progress, _renderCts.Token, reuseUnchangedSegments: true);

            // Surfaced so "still feels like CPU" can be checked against what the
            // render actually picked instead of guessed at from Task Manager -
            // see DescribeEffectiveEncoder's own doc comment.
            var encoderNote = PreviewRenderPlanFactory.DescribeEffectiveEncoder(_options.Render);
            ShowStatus(result.Success
                ? $"Preview ready: {previewPath} (encoder: {encoderNote})"
                : "Render failed: " + string.Join(" | ", result.Errors),
                result.Success ? StatusKind.Success : StatusKind.Error);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Render cancelled.");
        }
        catch (Exception ex)
        {
            ShowStatus("Build failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void BtnFinal_Click(object sender, RoutedEventArgs e)
    {
        if (_inventory is null || !IsProjectFolderReady())
        {
            return;
        }

        try
        {
            SetBusy(true, "Rendering final…");
            if (!await PlanIfNeededAsync())
            {
                return;
            }

            var finalDirectory = Path.Combine(_projectFolder, "out", "final");
            var renderDirectory = Path.Combine(finalDirectory, "render");
            var finalPath = Path.Combine(finalDirectory, "final.mp4");

            var finalOptions = FinalRenderOptions(_options);
            var plan = PreviewRenderPlanFactory.Build(
                _planned!.Timeline!, _inventory.AllImages, finalOptions,
                renderDirectory, finalPath);

            _renderCts = new CancellationTokenSource();
            var progress = new Progress<double>(p => PbRender.Value = p * 100);
            // Uses finalOptions.Render (not _options.Render) so the ffmpeg binary
            // that actually runs is resolved from the exact same RenderOptions
            // PreviewRenderPlanFactory.Build just used to pick the encoder for
            // this plan's segments - they happen to agree today since
            // FinalRenderOptions only overrides preview-preset fields, but
            // resolving from the options that were actually planned against, not
            // a separate copy, is what keeps that true if that ever changes.
            var service = new PreviewRenderService(
                new FfmpegRunner(PreviewRenderPlanFactory.ResolveEffectiveFfmpegPath(finalOptions.Render)));
            var result = await service.RenderAsync(
                plan, maxParallelism: 2, progress, _renderCts.Token, reuseUnchangedSegments: true);

            if (!result.Success)
            {
                ShowStatus("Final render failed: " + string.Join(" | ", result.Errors), StatusKind.Error);
                return;
            }

            var captionsPath = Path.Combine(finalDirectory, "captions.srt");
            var captionsSource = _inventory.SrtFilePath;
            var hasCaptions = captionsSource is not null;
            if (captionsSource is not null)
            {
                File.Copy(captionsSource, captionsPath, overwrite: true);
            }

            var encoderNote = PreviewRenderPlanFactory.DescribeEffectiveEncoder(finalOptions.Render);
            ShowStatus(
                "Final render ready: " + finalPath + (hasCaptions ? " + captions.srt" : "") +
                $" — drop both into CapCut (CapCut tier 1). (encoder: {encoderNote})",
                StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Render cancelled.");
        }
        catch (Exception ex)
        {
            ShowStatus("Final render failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private static ProjectOptions FinalRenderOptions(ProjectOptions options)
    {
        var clone = System.Text.Json.JsonSerializer.Deserialize<ProjectOptions>(
            System.Text.Json.JsonSerializer.Serialize(options, OptionsJson.JsonOptions),
            OptionsJson.JsonOptions)!;
        clone.Render.PreviewWidth = clone.Output.Width;
        clone.Render.PreviewHeight = clone.Output.Height;
        clone.Render.PreviewPreset = clone.Render.FinalPreset;
        clone.Render.PreviewCrf = clone.Render.FinalCrf;
        clone.Render.PreviewBframes = clone.Render.FinalBframes;
        return clone;
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_inventory is null || !IsProjectFolderReady())
        {
            return;
        }

        try
        {
            SetBusy(true, "Exporting…");
            if (!await PlanIfNeededAsync())
            {
                return;
            }

            var xmlPath = Path.Combine(_projectFolder, "out", "premiere.xml");
            var xml = Fcp7XmlExporter.Export(
                _planned!.Timeline!, _inventory.AllImages,
                new PremiereExportOptions { IncludeMotionKeyframes = true });
            File.WriteAllText(xmlPath, xml);
            ShowStatus($"Exported: {xmlPath} — import it into Premiere (File > Import).", StatusKind.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("Export failed: " + ex.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _renderCts?.Cancel();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        // Settings must start from what's actually saved on disk, not
        // whatever in-memory ProjectOptions this window happens to be
        // holding right now. _options stays at bare defaults until ANALYZE
        // has been clicked at least once in this run, so opening Settings
        // before that (a very easy thing to do right after browsing to a
        // project) used to mean its Save button would silently overwrite the
        // project's real config with those defaults - wiping out anything set
        // outside the dialog, such as a hand-edited render.ffmpeg_path, or
        // any field the dialog itself doesn't expose. Reloading here, right
        // before the dialog is built, closes that gap regardless of whether
        // the person remembered to Analyze first.
        if (_projectFolder.Length > 0 && Directory.Exists(_projectFolder))
        {
            var optionsPath = Path.Combine(_projectFolder, "imgtovideo.json");
            if (File.Exists(optionsPath))
            {
                try
                {
                    _options = OptionsJson.LoadOrDefault(optionsPath);
                }
                catch (IOException)
                {
                    // Keep whatever _options already held — better than losing it.
                }
                catch (InvalidDataException)
                {
                }
            }
        }

        ApplyOptionControls();
        var dialog = new SettingsWindow(_options, _projectFolder) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _options = dialog.Options;
            SyncOptionControls();

            // PlanIfNeededAsync (used by BUILD PREVIEW/RENDER FINAL/EXPORT) only
            // ever re-runs EditPlanner.Plan when _planned is null - it's a "plan
            // once per project load" cache, not a "plan once per settings" cache.
            // Motion, easing, transitions, pan/zoom percents, and every other
            // planning-affecting option live inside _planned.Timeline (baked in
            // as each clip's StartViewport/EndViewport/Easing/etc. at plan time),
            // so without this, saving new Settings here had no visible effect on
            // the next render at all: it silently kept using whichever plan was
            // computed from the options in effect the last time Analyze or a
            // scene-editor save ran, however different the just-saved settings
            // are. Clearing it forces the very next Build/Final/Export to replan
            // from the options just saved, exactly like re-running Analyze would.
            _planned = null;
            ShowStatus("Settings saved.", StatusKind.Success);
        }
    }

    private bool IsProjectFolderReady()
    {
        if (_projectFolder.Length == 0 || !Directory.Exists(_projectFolder))
        {
            ShowStatus("Select an existing project folder first.", StatusKind.Error);
            return false;
        }

        AppSettingsStore.Save(new AppSettings { LastProjectFolder = _projectFolder });
        return true;
    }

    private async Task<bool> PlanIfNeededAsync()
    {
        if (_planned is { Success: true })
        {
            return true;
        }

        if (_inventory?.AudioFilePath is null)
        {
            ShowStatus("No audio file — cannot plan.", StatusKind.Error);
            return false;
        }

        ApplyOptionControls();
        var optionsErrors = _options.Validate();
        if (optionsErrors.Count > 0)
        {
            ShowStatus("Invalid settings: " + string.Join(" ", optionsErrors), StatusKind.Error);
            return false;
        }

        var audioSeconds = await new Ffprobe(_options.Render.FfprobePath)
            .GetDurationSecondsAsync(_inventory.AudioFilePath);
        _planned = EditPlanner.Plan(_inventory, audioSeconds, _options);
        UpdateIssues(_planned.Issues);

        if (!_planned.Success)
        {
            ShowStatus("Build blocked: fix errors in diagnostics first.", StatusKind.Error);
            return false;
        }

        var outDir = Path.Combine(_projectFolder, "out");
        Directory.CreateDirectory(outDir);
        TimelineJson.Save(_planned!.Timeline!, Path.Combine(outDir, "timeline.json"));
        BuildReportWriter.Save(
            BuildReportFactory.Create(
                Path.GetFileName(_projectFolder), _planned.Timeline!, _options, _planned.Issues,
                _planned.Coverage),
            Path.Combine(outDir, "build-report.json"));

        // Reveal shots: write their partial stills (slices 1..k of the image) and
        // register them as images, so preview, export and the scene editor treat
        // them like any other image.
        var withReveal = await RevealImageWriter.EnsureAsync(
            _planned.Timeline!, _inventory.AllImages,
            new FfmpegRunner(PreviewRenderPlanFactory.ResolveEffectiveFfmpegPath(_options.Render)));
        var knownPaths = _inventory.AllImages
            .Select(i => i.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var image in withReveal.Where(i => !knownPaths.Contains(i.FilePath)).ToList())
        {
            _inventory.AllImages.Add(image);
        }

        // Sound effects named in the shotlist: project sfx\ folder first, then the built-in pack.
        var soundIssues = await SoundEffectResolver.ResolveAsync(
            _planned.Timeline!, _projectFolder,
            new FfmpegRunner(PreviewRenderPlanFactory.ResolveEffectiveFfmpegPath(_options.Render)),
            new Ffprobe(_options.Render.FfprobePath),
            _options.Sound.DefaultPop);
        if (soundIssues.Count > 0)
        {
            UpdateIssues(_planned.Issues.Concat(soundIssues).ToList());
        }

        return true;
    }

    private async Task ProbeAudioDurationAsync()
    {
        TxtDuration.Text = "—";
        if (_inventory?.AudioFilePath is null)
        {
            return;
        }

        try
        {
            var seconds = await new Ffprobe(_options.Render.FfprobePath)
                .GetDurationSecondsAsync(_inventory.AudioFilePath);
            TxtDuration.Text = FormatDuration(seconds);
        }
        catch (Exception ex)
        {
            AppendStatus("Audio probe failed: " + ex.Message);
        }
    }

    private void UpdateStats()
    {
        if (_inventory is null)
        {
            return;
        }

        TxtAudio.Text = _inventory.AudioFilePath is { } audio
            ? Path.GetFileName(audio) + "  ✓"
            : "missing  ✗";
        TxtSubtitle.Text = _inventory.SrtFilePath is { } srt
            ? Path.GetFileName(srt) + "  ✓"
            : "missing  ✗";
        var present = _inventory.AllImages.Count;
        var planned = _inventory.Manifest?.Assets.Count;
        TxtImages.Text = planned is > 0 ? $"{present} / {planned}" : $"{present}";
        TxtScenes.Text = $"{_inventory.SceneGroups.Count}";
        TxtWarningCount.Text =
            $"{_inventory.Issues.Count(i => i.Severity == ValidationSeverity.Warning)}";
    }

    private void UpdateIssues(IEnumerable<ValidationIssue> issues)
    {
        var allIssues = issues.ToList();

        LstIssues.Items.Clear();
        foreach (var group in DiagnosticsGrouper.Group(allIssues))
        {
            LstIssues.Items.Add(new DiagnosticsRow(
                $"{group.Title}  ({group.Summary()})",
                (System.Windows.Media.SolidColorBrush)FindResource("BrushTextSecondary"),
                (System.Windows.Media.SolidColorBrush)FindResource("BrushRecessed"),
                IsHeader: true));

            foreach (var issue in group.Issues)
            {
                var (textBrush, tintBrush) = issue.Severity switch
                {
                    ValidationSeverity.Error => ("BrushErrorFg", "BrushErrorBg"),
                    ValidationSeverity.Warning => ("BrushWarnFg", "BrushWarnBg"),
                    _ => ("BrushInfoFg", "BrushInfoBg"),
                };

                LstIssues.Items.Add(new DiagnosticsRow(
                    $"[{issue.Severity.ToString().ToUpperInvariant()}] {issue.Message}",
                    (System.Windows.Media.SolidColorBrush)FindResource(textBrush),
                    (System.Windows.Media.SolidColorBrush)FindResource(tintBrush)));
            }
        }

        if (_inventory is not null)
        {
            TxtWarningCount.Text =
                $"{allIssues.Count(i => i.Severity == ValidationSeverity.Warning)}";
        }
    }

    private void BtnCopyMissing_Click(object sender, RoutedEventArgs e) =>
        CopyMissing(plainText: false);

    private void BtnCopyMissingText_Click(object sender, RoutedEventArgs e) =>
        CopyMissing(plainText: true);

    private void CopyMissing(bool plainText)
    {
        if (_projectFolder.Length == 0 || !Directory.Exists(_projectFolder))
        {
            ShowStatus("Select an existing project folder first.", StatusKind.Error);
            return;
        }

        try
        {
            var result = MissingShotList.BuildForProject(_projectFolder);
            if (result is null)
            {
                ShowStatus("No shotlist.json in this project — nothing to copy.", StatusKind.Error);
                return;
            }

            if (result.Count == 0)
            {
                ShowStatus("No missing images — every planned image is on disk.", StatusKind.Success);
                return;
            }

            System.Windows.Clipboard.SetText(plainText ? result.PlainText : result.Json);
            var shape = plainText ? "plain text" : "shotlist JSON";
            ShowStatus(
                $"Copied {result.Count} missing image(s) as {shape} — paste into the image generator.",
                StatusKind.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("Copy missing failed: " + ex.Message, StatusKind.Error);
        }
    }

    private void BtnCopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(
            Environment.NewLine,
            LstIssues.Items.OfType<DiagnosticsRow>().Select(row => row.Text));
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            TxtStatus.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception)
        {
            TxtStatus.Text = "Could not access the clipboard; select and copy the text manually.";
        }
    }

    private void BtnCopySrtForLlm_Click(object sender, RoutedEventArgs e)
    {
        if (_inventory?.SrtFilePath is not { } srtPath)
        {
            ShowStatus("Analyze a project with a narration .srt first.", StatusKind.Error);
            return;
        }

        string text;
        try
        {
            text = SrtParser.ToCompactNarration(File.ReadAllText(srtPath));
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            ShowStatus($"Could not read {Path.GetFileName(srtPath)}: {ex.Message}", StatusKind.Error);
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
            ShowStatus(
                $"Copied {Path.GetFileName(srtPath)} without timestamps ({text.Length:N0} chars) — " +
                "paste it into the LLM with the authoring brief.",
                StatusKind.Success);
        }
        catch (Exception)
        {
            ShowStatus("Could not access the clipboard; select and copy the text manually.", StatusKind.Error);
        }
    }

    private void SyncOptionControls()
    {
        ChkAutoMotion.IsChecked = _options.Motion.AutoMotionEnabled;
        ChkTransitions.IsChecked = _options.Transitions.Enabled;
        TxtMinDuration.Text = F(_options.Timing.MinImageSeconds);
        TxtPreferredDuration.Text = F(_options.Timing.PreferredImageSeconds);
    }

    private void ApplyOptionControls()
    {
        _options.Motion.AutoMotionEnabled = ChkAutoMotion.IsChecked == true;
        _options.Transitions.Enabled = ChkTransitions.IsChecked == true;
        if (TryParse(TxtMinDuration.Text, out var min))
        {
            _options.Timing.MinImageSeconds = min;
        }

        if (TryParse(TxtPreferredDuration.Text, out var preferred))
        {
            _options.Timing.PreferredImageSeconds = preferred;
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        BtnAnalyze.IsEnabled = !busy;
        BtnSettings.IsEnabled = !busy;
        BtnScenes.IsEnabled = !busy && _inventory is not null;
        BtnCancel.IsEnabled = busy;

        if (busy)
        {
            BtnBuild.IsEnabled = false;
            BtnFinal.IsEnabled = false;
            BtnExport.IsEnabled = false;
            PbRender.Value = 0;
        }
        else
        {
            var blocked = _inventory is null
                || ValidationIssue.HasErrors(_inventory.Issues)
                || _planned is { Success: false };
            BtnBuild.IsEnabled = !blocked;
            BtnFinal.IsEnabled = !blocked;
            BtnExport.IsEnabled = !blocked;
            PbRender.Value = 0;
        }

        if (status is not null)
        {
            ShowStatus(status);
        }
    }

    private void ShowStatus(string message) => ShowStatus(message, StatusKind.Neutral);

    private void ShowStatus(string message, StatusKind kind)
    {
        TxtStatus.Text = message;
        TxtStatus.Foreground = kind switch
        {
            StatusKind.Success => (System.Windows.Media.SolidColorBrush)FindResource("BrushStatusSuccess"),
            StatusKind.Error => (System.Windows.Media.SolidColorBrush)FindResource("BrushStatusError"),
            _ => (System.Windows.Media.SolidColorBrush)FindResource("BrushTextPrimary"),
        };
    }

    private void AppendStatus(string message)
    {
        TxtStatus.Text = TxtStatus.Text + "  " + message;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string F(double value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
