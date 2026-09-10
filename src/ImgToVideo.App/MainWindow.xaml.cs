using System.Globalization;
using System.IO;
using System.Windows;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Serialization;
using ImgToVideo.Ffmpeg;
using ImgToVideo.Premiere;
using Microsoft.Win32;

namespace ImgToVideo.App;

public partial class MainWindow : Window
{
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

            _options = OptionsJson.LoadOrDefault(Path.Combine(_projectFolder, "imgtovideo.json"));
            SyncOptionControls();
            _planned = null;

            _inventory = ProjectLoader.Load(_projectFolder, _options);
            UpdateStats();
            UpdateIssues(_inventory.Issues);

            ShowStatus(ValidationIssue.HasErrors(_inventory.Issues)
                ? "Analysis found blocking errors — see diagnostics."
                : $"Analysis complete: {_inventory.AllImages.Count} images in {_inventory.SceneGroups.Count} scenes.");

            await ProbeAudioDurationAsync();
        }
        catch (Exception ex)
        {
            ShowStatus("Analyze failed: " + ex.Message);
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
            var result = await service.RenderAsync(plan, maxParallelism: 2, progress, _renderCts.Token);

            ShowStatus(result.Success
                ? $"Preview ready: {previewPath}"
                : "Render failed: " + string.Join(" | ", result.Errors));
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Render cancelled.");
        }
        catch (Exception ex)
        {
            ShowStatus("Build failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false, null);
        }
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
            ShowStatus($"Exported: {xmlPath} — import it into Premiere (File > Import).");
        }
        catch (Exception ex)
        {
            ShowStatus("Export failed: " + ex.Message);
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
            ShowStatus("Settings saved.");
        }
    }

    private bool IsProjectFolderReady()
    {
        if (_projectFolder.Length == 0 || !Directory.Exists(_projectFolder))
        {
            ShowStatus("Select an existing project folder first.");
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
            ShowStatus("No audio file — cannot plan.");
            return false;
        }

        ApplyOptionControls();
        var optionsErrors = _options.Validate();
        if (optionsErrors.Count > 0)
        {
            ShowStatus("Invalid settings: " + string.Join(" ", optionsErrors));
            return false;
        }

        var audioSeconds = await new Ffprobe(_options.Render.FfprobePath)
            .GetDurationSecondsAsync(_inventory.AudioFilePath);
        _planned = EditPlanner.Plan(_inventory, audioSeconds, _options);
        UpdateIssues(_planned.Issues);

        if (!_planned.Success)
        {
            ShowStatus("Build blocked: fix errors in diagnostics first.");
            return false;
        }

        var outDir = Path.Combine(_projectFolder, "out");
        Directory.CreateDirectory(outDir);
        TimelineJson.Save(_planned.Timeline!, Path.Combine(outDir, "timeline.json"));
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
            LstIssues.Items.Add($"[{issue.Severity.ToString().ToUpperInvariant()}] {issue.Message}");
        }

        if (_inventory is not null)
        {
            TxtWarningCount.Text =
                $"{issues.Count(i => i.Severity == ValidationSeverity.Warning)}";
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
        BtnCancel.IsEnabled = busy;

        if (busy)
        {
            BtnBuild.IsEnabled = false;
            BtnExport.IsEnabled = false;
            PbRender.Value = 0;
        }
        else
        {
            var blocked = _inventory is null
                || ValidationIssue.HasErrors(_inventory.Issues)
                || _planned is { Success: false };
            BtnBuild.IsEnabled = !blocked;
            BtnExport.IsEnabled = !blocked;
            PbRender.Value = 0;
        }

        if (status is not null)
        {
            ShowStatus(status);
        }
    }

    private void ShowStatus(string message)
    {
        TxtStatus.Text = message;
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
