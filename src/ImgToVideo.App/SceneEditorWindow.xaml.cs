using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using ImgToVideo.Ffmpeg;

namespace ImgToVideo.App;

public partial class SceneEditorWindow : Window
{
    public sealed class ClipRow
    {
        public string FilePath { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
        public string Motion { get; set; } = "Auto";
        public string Easing { get; set; } = "Auto";
        public string Duration { get; set; } = "0";
        public bool Exclude { get; set; }
        public string Transition { get; set; } = "Auto";
        public long PlannedDuration { get; set; }
        public bool IsFirstClipOfVideo { get; set; }
    }

    private readonly Timeline _timeline;
    private readonly ProjectInventory _inventory;
    private readonly ProjectOptions _options;
    private readonly string _projectFolder;
    private readonly Dictionary<string, ObservableCollection<ClipRow>> _rowsByScene = new();
    private bool _saved;

    public bool Saved => _saved;

    public SceneEditorWindow(
        string projectFolder, Timeline timeline, ProjectInventory inventory, ProjectOptions options)
    {
        InitializeComponent();
        _projectFolder = projectFolder;
        _timeline = timeline;
        _inventory = inventory;
        _options = options;

        BuildRows();
        CmbScene.ItemsSource = _timeline.Scenes.Select(s => s.Id).ToList();
        CmbScene.SelectedIndex = 0;
    }

    private void BuildRows()
    {
        foreach (var scene in _timeline.Scenes)
        {
            var rows = new ObservableCollection<ClipRow>();
            foreach (var clip in scene.Clips)
            {
                var clipOverride = _inventory.Overrides.ForClip(clip.FilePath);

                rows.Add(new ClipRow
                {
                    FilePath = clip.FilePath,
                    Display = Path.GetFileName(clip.FilePath),
                    Motion = clipOverride?.Motion is { } motion ? CodeOf(motion) : "Auto",
                    Easing = clipOverride?.Easing is { } easing ? NameOf(easing) : "Auto",
                    Duration = (clipOverride?.DurationFrames ?? clip.DurationFrames)
                        .ToString(CultureInfo.InvariantCulture),
                    PlannedDuration = clip.DurationFrames,
                    Exclude = clipOverride?.Exclude ?? false,
                    Transition = _inventory.Overrides.CutFor(clip.FilePath) is { } cut
                        ? TransitionCatalog.NameOf(cut) ?? "Auto"
                        : "Auto",
                    IsFirstClipOfVideo = scene == _timeline.Scenes[0] && scene.Clips[0] == clip,
                });
            }

            _rowsByScene[scene.Id] = rows;
        }
    }

    private static string CodeOf(MotionType motion) =>
        MotionCodes.All.FirstOrDefault(m => m.Motion == motion).Suffix ?? "Auto";

    private static string NameOf(EasingMode easing) =>
        EasingModes.NameOf(easing) ?? "Auto";

    private ObservableCollection<ClipRow>? SelectedRows =>
        CmbScene.SelectedValue is string id ? _rowsByScene.GetValueOrDefault(id) : null;

    private void CmbScene_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClipList.ItemsSource = SelectedRows;
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        MoveRow(sender, -1);
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        MoveRow(sender, 1);
    }

    private void MoveRow(object sender, int delta)
    {
        if (SelectedRows is null || (sender as FrameworkElement)?.Tag is not ClipRow row)
        {
            return;
        }

        var index = SelectedRows.IndexOf(row);
        var target = index + delta;
        if (target < 0 || target >= SelectedRows.Count)
        {
            return;
        }

        SelectedRows.Move(index, target);
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var result = BuildOverrides();
        try
        {
            OverridesJson.Save(result, Path.Combine(_projectFolder, "overrides.json"));
            _saved = true;
            TxtEditorStatus.Text = $"Overrides saved to {Path.Combine(_projectFolder, "overrides.json")} — " +
                                   "BUILD PREVIEW to apply.";
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, "Saving failed: " + ex.Message, "ImgToVideo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ProjectOverrides BuildOverrides()
    {
        var result = new ProjectOverrides();
        var sceneLookup = _timeline.Scenes.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (sceneId, rows) in _rowsByScene)
        {
            var scene = sceneLookup.GetValueOrDefault(sceneId);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var clipOverride = new ClipOverride { File = row.FilePath };
                var changed = false;

                if (row.Exclude)
                {
                    clipOverride.Exclude = true;
                    changed = true;
                }

                if (row.Motion != "Auto" && MotionCodes.TryFromSuffix(row.Motion, out var motion))
                {
                    clipOverride.Motion = motion;
                    changed = true;
                }

                if (row.Easing != "Auto" && EasingModes.TryFromName(row.Easing, out var easing))
                {
                    clipOverride.Easing = easing;
                    changed = true;
                }

                if (long.TryParse(row.Duration, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var duration) && duration > 0 && duration != row.PlannedDuration)
                {
                    clipOverride.DurationFrames = duration;
                    changed = true;
                }

                var naturalIndex = scene?.Clips.FindIndex(
                    c => string.Equals(c.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase)) ?? -1;
                if (naturalIndex >= 0 && i != naturalIndex)
                {
                    clipOverride.Order = i;
                    changed = true;
                }

                if (changed)
                {
                    result.Clips.Add(clipOverride);
                }

                if (row.Transition != "Auto" && !row.IsFirstClipOfVideo &&
                    TransitionCatalog.TryFromName(row.Transition, out var transition))
                {
                    result.Cuts.Add(new CutOverride
                    {
                        BeforeFile = row.FilePath,
                        Transition = transition,
                    });
                }
            }
        }

        return result;
    }

    private async void BtnPreviewRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ClipRow row || sender is not Button previewButton)
        {
            return;
        }

        var scene = _timeline.Scenes.FirstOrDefault(s => s.Clips.Any(c =>
            string.Equals(c.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase)));
        var clip = scene?.Clips.FirstOrDefault(c =>
            string.Equals(c.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase));
        var image = _inventory.AllImages.FirstOrDefault(i =>
            string.Equals(i.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase));

        if (scene is null || clip is null || image is null || image.Width <= 0)
        {
            TxtEditorStatus.Text = "Cannot preview this clip (missing image or dimensions).";
            return;
        }

        previewButton.IsEnabled = false;
        TxtEditorStatus.Text = "Rendering clip…";

        try
        {
            var previewClip = BuildPreviewClip(row, clip, image.Width, image.Height);
            var clipsDirectory = Path.Combine(_projectFolder, "out", "clips");
            Directory.CreateDirectory(clipsDirectory);
            var videoPath = Path.Combine(
                clipsDirectory, Path.GetFileNameWithoutExtension(clip.FilePath) + ".mp4");

            var runner = new FfmpegRunner(_options.Render.FfmpegPath);
            var args = PreviewRenderPlanFactory.BuildClipPreviewArguments(
                previewClip, image.Width, image.Height, _options, videoPath);
            var video = await runner.RunAsync(args);
            if (!video.Success)
            {
                TxtEditorStatus.Text = "Preview failed: " + video.ErrorTail;
                return;
            }

            if (_inventory.AudioFilePath is { } audio)
            {
                var fps = _options.Output.Fps;
                var muxArgs = PreviewRenderPlanFactory.BuildClipPreviewMuxArguments(
                    videoPath, audio,
                    clip.StartFrame / fps,
                    previewClip.DurationFrames / fps,
                    videoPath);
                var muxed = await runner.RunAsync(muxArgs);
                if (!muxed.Success)
                {
                    TxtEditorStatus.Text = "Preview rendered without audio (mux failed): " + muxed.ErrorTail;
                }
            }

            TxtEditorStatus.Text = "Clip preview ready.";
            new ClipPreviewPlayerWindow(row.Display, videoPath) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            TxtEditorStatus.Text = "Preview failed: " + ex.Message;
        }
        finally
        {
            previewButton.IsEnabled = true;
        }
    }

    private VideoClip BuildPreviewClip(ClipRow row, VideoClip clip, int imageWidth, int imageHeight)
    {
        var motion = row.Motion != "Auto" && MotionCodes.TryFromSuffix(row.Motion, out var overridden)
            ? overridden
            : clip.Motion;
        var easing = row.Easing != "Auto" && EasingModes.TryFromName(row.Easing, out var eased)
            ? eased
            : clip.Easing;
        var duration = long.TryParse(row.Duration, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var frames) && frames > 0
            ? frames
            : clip.DurationFrames;

        var engine = new MotionEngine(_options.Motion, _options.Output);
        var plan = engine.PlanClip(motion, MotionSource.Override, panRight: true, imageWidth, imageHeight);

        return new VideoClip
        {
            FilePath = clip.FilePath,
            SceneId = clip.SceneId,
            StartFrame = clip.StartFrame,
            DurationFrames = duration,
            Motion = plan.Motion,
            MotionSource = plan.Motion == motion ? MotionSource.Override : clip.MotionSource,
            ImageType = clip.ImageType,
            Easing = easing,
            StartViewport = plan.Start,
            EndViewport = plan.End,
        };
    }
}
