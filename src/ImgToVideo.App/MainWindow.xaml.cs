using System.Globalization;
using System.IO;
using System.Windows;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Reporting;
using ImgToVideo.Core.Serialization;
using ImgToVideo.Ffmpeg;
using ImgToVideo.Premiere;
using Microsoft.Win32;

namespace ImgToVideo.App;

public partial class MainWindow : Window
{
    private sealed record DiagnosticsRow(string Text, System.Windows.Media.SolidColorBrush TextBrush, System.Windows.Media.SolidColorBrush TintBrush);

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
                try
                {
                    OptionsJson.Save(new ProjectOptions(), optionsPath);
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
            var service = new PreviewRenderService(new FfmpegRunner(_options.Render.FfmpegPath));
            var result = await service.RenderAsync(
                plan, maxParallelism: 2, progress, _renderCts.Token, reuseUnchangedSegments: true);

            ShowStatus(result.Success
                ? $"Preview ready: {previewPath}"
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

            var plan = PreviewRenderPlanFactory.Build(
                _planned!.Timeline!, _inventory.AllImages, FinalRenderOptions(_options),
                renderDirectory, finalPath);

            _renderCts = new CancellationTokenSource();
            var progress = new Progress<double>(p => PbRender.Value = p * 100);
            var service = new PreviewRenderService(new FfmpegRunner(_options.Render.FfmpegPath));
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

            ShowStatus(
                "Final render ready: " + finalPath + (hasCaptions ? " + captions.srt" : "") +
                " — drop both into CapCut (CapCut tier 1).",
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
        ApplyOptionControls();
        var dialog = new SettingsWindow(_options, _projectFolder) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _options = dialog.Options;
            SyncOptionControls();
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
        TxtImages.Text = $"{_inventory.AllImages.Count}";
        TxtScenes.Text = $"{_inventory.SceneGroups.Count}";
        TxtWarningCount.Text =
            $"{_inventory.Issues.Count(i => i.Severity == ValidationSeverity.Warning)}";
    }

    private void UpdateIssues(IEnumerable<ValidationIssue> issues)
    {
        LstIssues.Items.Clear();
        foreach (var issue in issues)
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

        if (_inventory is not null)
        {
            TxtWarningCount.Text =
                $"{issues.Count(i => i.Severity == ValidationSeverity.Warning)}";
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
