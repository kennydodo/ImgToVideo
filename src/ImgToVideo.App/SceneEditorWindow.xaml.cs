using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Motion;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using ImgToVideo.Ffmpeg;
using Rect = ImgToVideo.Core.Models.Rect;

namespace ImgToVideo.App;

public partial class SceneEditorWindow : Window
{
    private const double MaxNudgeFrames = 120;
    private const double TrimBoxWidth = 256;
    private const double TrimBoxHeight = 144;

    public sealed class ClipRow : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
        public string Motion { get; set; } = "Auto";
        public string Easing { get; set; } = "Auto";

        private string _duration = "0";
        public string Duration
        {
            get => _duration;
            set
            {
                if (_duration == value)
                {
                    return;
                }

                _duration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
            }
        }

        public bool Exclude { get; set; }
        public string Transition { get; set; } = "Auto";
        public long PlannedDuration { get; set; }
        public bool IsFirstClipOfVideo { get; set; }
        public double Nudge { get; set; }
        public bool CanNudge { get; set; } = true;

        /// <summary>Manifest shot id; set for v2-planned rows.</summary>
        public string? ShotId { get; set; }

        public long StartFrame { get; set; }

        public double Fps { get; set; }

        private string _nudgeLabel = string.Empty;
        public string NudgeLabel
        {
            get => _nudgeLabel;
            set
            {
                if (_nudgeLabel == value)
                {
                    return;
                }

                _nudgeLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NudgeLabel)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record PreviewRenderOutcome(
        string? PlayPath, bool MuxFailed, string? ErrorTail, string? Note = null);

    private sealed record TransitionPreview(TransitionIn Transition, VideoClip Outgoing, ImageInfo OutgoingImage);

    private readonly Timeline _timeline;
    private readonly ProjectInventory _inventory;
    private readonly ProjectOptions _options;
    private readonly string _projectFolder;
    private readonly Dictionary<string, ObservableCollection<ClipRow>> _rowsByScene = new();
    private ClipPreviewPlayerWindow? _floatingPlayer;
    private bool _updatingNudge;
    private bool _nudgeDragging;
    private bool _saved;
    private List<(double Start, double End, string Label)> _overlayCues = new();
    private string? _overlaySceneId;
    private readonly List<Border> _stripSegments = new();
    private List<ClipRow> _stripVisibleRows = new();
    private ClipRow? _stripDragRow;
    private ClipRow? _stripDragNext;
    private long _stripDragTotal;
    private int _stripDragColumn;
    private double _stripDragAccum;
    private bool _stripDragging;
    private readonly Dictionary<string, BitmapSource> _trimBitmaps = new(StringComparer.OrdinalIgnoreCase);

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
        PlayerHost.PositionChanged += (_, seconds) => UpdateOverlay(seconds);
    }

    private void SetOverlayCues(IEnumerable<VideoClip> clips)
    {
        var fps = _options.Output.Fps;
        double cursor = 0;
        var cues = new List<(double Start, double End, string Label)>();
        foreach (var clip in clips)
        {
            var duration = clip.DurationFrames / fps;
            cues.Add((cursor, cursor + duration, OverlayLabel(clip)));
            cursor += duration;
        }

        _overlayCues = cues;
        PlayerHost.SetOverlay(null);
    }

    private static string OverlayLabel(VideoClip clip) =>
        clip.ShotId is null
            ? Path.GetFileName(clip.FilePath)
            : $"{clip.ShotId} · {Path.GetFileName(clip.FilePath)}";

    private void UpdateOverlay(double seconds)
    {
        if (_overlayCues.Count == 0)
        {
            return;
        }

        var activeIndex = -1;
        for (var i = 0; i < _overlayCues.Count; i++)
        {
            if (seconds + 0.0001 >= _overlayCues[i].Start)
            {
                activeIndex = i;
            }
            else
            {
                break;
            }
        }

        PlayerHost.SetOverlay(activeIndex >= 0 ? _overlayCues[activeIndex].Label : null);
        HighlightStripSegment(_overlaySceneId is not null && _overlaySceneId == CurrentStripToken()
            ? activeIndex
            : -1);
    }

    private void RefreshStrip()
    {
        StripGrid.Children.Clear();
        StripGrid.ColumnDefinitions.Clear();
        _stripSegments.Clear();

        if (CurrentRows() is not { } rows || rows.Count == 0)
        {
            return;
        }

        var visible = rows.Where(r => !r.Exclude).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var durations = visible
            .Select(r => TryParseFrames(r.Duration, out var frames) && frames > 0 ? frames : 0)
            .ToList();
        var total = durations.Sum();
        if (total <= 0)
        {
            return;
        }

        var panelBrush = TryFindResource("BrushPanel") as Brush;
        var hoverBrush = TryFindResource("BrushHover") as System.Windows.Media.Brush;
        var textBrush = TryFindResource("BrushTextSecondary") as Brush;
        var accentBrush = TryFindResource("BrushAccent") as Brush;
        _stripVisibleRows = visible;
        var fps = _options.Output.Fps;
        var cumulative = 0L;

        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            var startSeconds = cumulative / fps;
            StripGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(durations[i], GridUnitType.Star),
            });

            var segment = new Border
            {
                Background = i % 2 == 0 ? panelBrush : hoverBrush,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(1, 0, 1, 0),
                Opacity = 0.65,
                ToolTip = $"{row.Display} — {durations[i]} frames ({durations[i] / fps:F1} s) — click to jump here",
                Child = new TextBlock
                {
                    Text = row.ShotId is null ? (i + 1).ToString() : row.ShotId.Replace("shot-", "#"),
                    Foreground = textBrush,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            segment.MouseLeftButtonDown += (_, e) =>
            {
                PlayerHost.PausePlayback();
                PlayerHost.SeekToSeconds(startSeconds);
                e.Handled = true;
            };
            Grid.SetColumn(segment, i);
            StripGrid.Children.Add(segment);
            _stripSegments.Add(segment);
            cumulative += durations[i];

            if (i == visible.Count - 1)
            {
                continue;
            }

            var handle = new Border
            {
                Width = 14,
                Background = TryFindResource("BrushSecondaryBorder") as Brush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(-7, 0, -7, 0),
                CornerRadius = new CornerRadius(2),
                Cursor = Cursors.SizeWE,
                ToolTip = "Drag to move this cut",
                Tag = (row, visible[i + 1], i),
            };
            handle.MouseEnter += (_, _) => handle.Background = TryFindResource("BrushAccent") as Brush;
            handle.MouseLeave += (_, _) =>
            {
                if (!_stripDragging)
                {
                    handle.Background = TryFindResource("BrushSecondaryBorder") as Brush;
                }
            };
            handle.MouseLeftButtonDown += StripHandle_MouseLeftButtonDown;
            handle.MouseMove += StripHandle_MouseMove;
            handle.MouseLeftButtonUp += StripHandle_MouseLeftButtonUp;
            Grid.SetColumn(handle, i + 1);
            Panel.SetZIndex(handle, 2);
            StripGrid.Children.Add(handle);
        }
    }

    private void StripHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border handle || handle.Tag is not (ClipRow left, ClipRow right, int column))
        {
            return;
        }

        _stripDragRow = left;
        _stripDragNext = right;
        _stripDragColumn = column;
        _stripDragTotal = (TryParseFrames(left.Duration, out var d) && d > 0 ? d : 1) +
                          (TryParseFrames(right.Duration, out var n) && n > 0 ? n : 1);
        _stripDragging = true;
        PlayerHost.PausePlayback();
        PlayerHost.BeginScrub();
        handle.CaptureMouse();
        handle.Background = TryFindResource("BrushAccent") as Brush;
        e.Handled = true;
    }

    private void StripHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_stripDragging || StripGrid.ActualWidth < 1 ||
            _stripDragColumn + 1 >= StripGrid.ColumnDefinitions.Count)
        {
            return;
        }

        // The cut follows the cursor exactly: cursor position -> fraction of
        // the strip -> frame within the row+next pair.
        var px = e.GetPosition(StripGrid).X;
        ApplyStripBoundary(Math.Clamp(
            (long)Math.Round(px / StripGrid.ActualWidth * _stripDragTotal),
            1, Math.Max(1, _stripDragTotal - 1)));
        if (TrimPreviewHost.Visibility != Visibility.Visible)
        {
            UpdateTrimPreview();
        }
    }

    private void ApplyStripBoundary(long boundary)
    {
        if (TryParseFrames(_stripDragRow!.Duration, out var current) && current == boundary)
        {
            return;
        }

        _stripDragRow.Duration = boundary.ToString(CultureInfo.InvariantCulture);
        _stripDragNext.Duration = (_stripDragTotal - boundary).ToString(CultureInfo.InvariantCulture);
        _stripDragRow.NudgeLabel = NudgeLabelFor(_stripDragRow);
        _stripDragNext.NudgeLabel = NudgeLabelFor(_stripDragNext);
        StripGrid.ColumnDefinitions[_stripDragColumn].Width =
            new GridLength(boundary, GridUnitType.Star);
        StripGrid.ColumnDefinitions[_stripDragColumn + 1].Width =
            new GridLength(_stripDragTotal - boundary, GridUnitType.Star);
        UpdateTrimPreview();
    }

    private void StripHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_stripDragging && sender is Border handle)
        {
            handle.ReleaseMouseCapture();
            handle.Background = TryFindResource("BrushSecondaryBorder") as Brush;
            _stripDragging = false;
            TrimPreviewHost.Visibility = Visibility.Collapsed;
            RefreshStrip();
            PlayerHost.EndScrub();
            _stripDragRow = null;
            _stripDragNext = null;
            e.Handled = true;
        }
    }

    private void UpdateTrimPreview()
    {
        if (_stripDragRow is not { } left || _stripDragNext is not { } right)
        {
            return;
        }

        var boundary = TryParseFrames(left.Duration, out var frames) && frames > 0 ? frames : 1;
        var outgoing = TryRenderTrimSide(TrimOutImage, TxtTrimOut, left, Math.Max(0, boundary - 1));
        var incoming = TryRenderTrimSide(TrimInImage, TxtTrimIn, right, 0);
        if (!outgoing && !incoming)
        {
            TrimPreviewHost.Visibility = Visibility.Collapsed;
            return;
        }

        TrimPreviewHost.Visibility = Visibility.Visible;
        TxtTrimHeader.Text =
            $"Cut @ {FormatTimecode((left.StartFrame + boundary) / _options.Output.Fps)} — outgoing last frame · incoming first frame";
        ScrubMediaToCut();
    }

    /// <summary>Scrubs the loaded preview to the moving cut so the video follows the drag.</summary>
    private void ScrubMediaToCut()
    {
        if (_stripDragRow is not { } row || _stripDragColumn >= _stripVisibleRows.Count)
        {
            return;
        }

        var prefix = 0L;
        for (var k = 0; k < _stripDragColumn; k++)
        {
            prefix += TryParseFrames(_stripVisibleRows[k].Duration, out var frames) && frames > 0 ? frames : 0;
        }

        var boundary = TryParseFrames(row.Duration, out var parsed) && parsed > 0 ? parsed : 1;
        PlayerHost.ScrubToSeconds((prefix + boundary) / _options.Output.Fps);
    }

    private bool TryRenderTrimSide(Image target, TextBlock label, ClipRow row, long frame)
    {
        if (ResolveRow(row) is not { } resolved)
        {
            target.Source = null;
            label.Text = $"{row.Display} — unavailable";
            return false;
        }

        var (clip, image) = resolved;
        if (GetTrimBitmap(image.FilePath) is not { } source)
        {
            target.Source = null;
            label.Text = $"{row.Display} — unreadable image";
            return false;
        }

        var planned = BuildPreviewClip(row, clip, image.Width, image.Height);
        var outAspect = (double)_options.Output.Width / _options.Output.Height;
        var startAspect = planned.StartViewport.Width / planned.StartViewport.Height;
        var endAspect = planned.EndViewport.Width / planned.EndViewport.Height;
        var letterbox = Math.Abs(startAspect - outAspect) > 0.02 ||
                        Math.Abs(endAspect - outAspect) > 0.02;
        var imageBounds = new Rect(0, 0, image.Width, image.Height);
        var standard = !letterbox &&
                       planned.StartViewport.IsInside(imageBounds) &&
                       planned.EndViewport.IsInside(imageBounds);
        var canvas = new Rect(0, 0, 0, 0);
        if (!letterbox && !standard)
        {
            var canvasWidth = Even(Math.Max(
                Math.Max(planned.StartViewport.Right, planned.EndViewport.Right), image.Width));
            var canvasHeight = Even(Math.Max(
                Math.Max(planned.StartViewport.Bottom, planned.EndViewport.Bottom), image.Height));
            canvas = new Rect(
                (canvasWidth - image.Width) / 2.0,
                (canvasHeight - image.Height) / 2.0,
                canvasWidth, canvasHeight);
        }

        double scale;
        double offsetX;
        double offsetY;
        if (letterbox)
        {
            scale = Math.Min(TrimBoxWidth / source.PixelWidth, TrimBoxHeight / source.PixelHeight);
            offsetX = (TrimBoxWidth - source.PixelWidth * scale) / 2.0;
            offsetY = (TrimBoxHeight - source.PixelHeight * scale) / 2.0;
        }
        else
        {
            var viewport = ClampViewport(
                ViewportAt(planned, frame),
                standard ? image.Width : canvas.Width,
                standard ? image.Height : canvas.Height);
            scale = TrimBoxWidth / viewport.Width;
            offsetX = ((standard ? 0 : canvas.X) - viewport.X) * scale;
            offsetY = ((standard ? 0 : canvas.Y) - viewport.Y) * scale;
        }

        target.Width = source.PixelWidth;
        target.Height = source.PixelHeight;
        target.Source = source;
        target.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform(offsetX, offsetY),
            },
        };
        label.Text = frame == 0
            ? $"{row.Display} — first frame"
            : $"{row.Display} — frame {frame + 1}/{planned.DurationFrames}";
        return true;
    }

    /// <summary>Mirrors the renderer's zoompan math: the viewport at a frame of the clip.</summary>
    private static Rect ViewportAt(VideoClip clip, long frame)
    {
        var duration = Math.Max(1, clip.DurationFrames);
        var steps = duration > 1 ? duration - 1 : 1;
        var motionSteps = clip.MotionDurationFrames is { } motionEnd && motionEnd > 1
            ? Math.Max(1, Math.Min(motionEnd - 1, steps))
            : steps;
        var progress = (double)frame / motionSteps;
        if (motionSteps != steps)
        {
            progress = Math.Min(1, progress);
        }

        progress = Math.Clamp(progress, 0, 1);
        progress = clip.Easing switch
        {
            EasingMode.EaseIn => progress * progress,
            EasingMode.EaseOut => 1 - (1 - progress) * (1 - progress),
            EasingMode.EaseInOut => progress * progress * progress * (progress * (progress * 6 - 15) + 10),
            _ => progress,
        };

        var start = clip.StartViewport;
        var end = clip.EndViewport;
        var width = start.Width + (end.Width - start.Width) * progress;
        var height = start.Height + (end.Height - start.Height) * progress;
        var centerX = start.X + start.Width / 2.0 + (end.X - start.X) * progress;
        var centerY = start.Y + start.Height / 2.0 + (end.Y - start.Y) * progress;
        return new Rect(centerX - width / 2.0, centerY - height / 2.0, width, height);
    }

    private static Rect ClampViewport(Rect viewport, double spaceWidth, double spaceHeight)
    {
        var x = Math.Clamp(viewport.X, 0, Math.Max(0, spaceWidth - viewport.Width));
        var y = Math.Clamp(viewport.Y, 0, Math.Max(0, spaceHeight - viewport.Height));
        return new Rect(x, y, viewport.Width, viewport.Height);
    }

    private static long Even(double value) =>
        (long)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2;

    private BitmapSource? GetTrimBitmap(string path)
    {
        if (_trimBitmaps.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            _trimBitmaps[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    private void HighlightStripSegment(int index)
    {
        var accentBrush = TryFindResource("BrushAccent") as Brush;
        for (var i = 0; i < _stripSegments.Count; i++)
        {
            var segment = _stripSegments[i];
            if (i == index)
            {
                segment.Opacity = 1.0;
                segment.BorderBrush = accentBrush;
                segment.BorderThickness = new Thickness(2);
            }
            else
            {
                segment.Opacity = 0.65;
                segment.BorderThickness = new Thickness(0);
            }
        }
    }

    private void DurationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ClipRow row)
        {
            row.NudgeLabel = NudgeLabelFor(row);
        }

        // A strip drag updates Durations programmatically; rebuilding the strip
        // here would destroy the Thumb that is capturing the mouse mid-drag.
        if (!_stripDragging)
        {
            RefreshStrip();
        }
    }

    private void ExcludeBox_Changed(object sender, RoutedEventArgs e) => RefreshStrip();

    private void BuildRows()
    {
        for (var sceneIndex = 0; sceneIndex < _timeline.Scenes.Count; sceneIndex++)
        {
            var scene = _timeline.Scenes[sceneIndex];
            var rows = new ObservableCollection<ClipRow>();
            for (var clipIndex = 0; clipIndex < scene.Clips.Count; clipIndex++)
            {
                var clip = scene.Clips[clipIndex];
                var clipOverride = clip.ShotId is null
                    ? _inventory.Overrides.ForClip(clip.FilePath)
                    : _inventory.Overrides.ForShot(clip.ShotId);

                rows.Add(new ClipRow
                {
                    FilePath = clip.FilePath,
                    Display = clip.ShotId is null
                        ? Path.GetFileName(clip.FilePath)
                        : $"{clip.ShotId} · {Path.GetFileName(clip.FilePath)}",
                    Motion = clipOverride?.Motion is { } motion ? CodeOf(motion)
                        : clip.ShotId is null ? "Auto"
                        : CodeOf(clip.Motion),
                    Easing = clipOverride?.Easing is { } easing ? NameOf(easing)
                        : clip.ShotId is null ? "Auto"
                        : NameOf(clip.Easing) ?? "Auto",
                    Duration = (clipOverride?.DurationFrames ?? clip.DurationFrames)
                        .ToString(CultureInfo.InvariantCulture),
                    PlannedDuration = clip.DurationFrames,
                    Exclude = clipOverride?.Exclude ?? false,
                    Transition = clip.ShotId is null
                        ? _inventory.Overrides.CutFor(clip.FilePath) is { } cut
                            ? TransitionCatalog.NameOf(cut) ?? "Auto"
                            : "Auto"
                        : clip.Transition is { } transition
                            ? TransitionCatalog.NameOf(transition.Kind) ?? "Auto"
                            : "Cut",
                    IsFirstClipOfVideo = sceneIndex == 0 && clipIndex == 0,
                    CanNudge = clipIndex < scene.Clips.Count - 1 || sceneIndex < _timeline.Scenes.Count - 1,
                    ShotId = clip.ShotId,
                    StartFrame = clip.StartFrame,
                    Fps = _options.Output.Fps,
                });
            }

            foreach (var rowEntry in rows)
            {
                rowEntry.NudgeLabel = NudgeLabelFor(rowEntry);
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

    private bool ShowAllScenes => ChkAllScenes.IsChecked == true;

    private ObservableCollection<ClipRow>? CurrentRows() =>
        ShowAllScenes ? new ObservableCollection<ClipRow>(FlatRows()) : SelectedRows;

    private string? CurrentStripToken() => ShowAllScenes ? "all" : CmbScene.SelectedValue as string;

    private List<ClipRow> FlatRows() => _timeline.Scenes
        .Where(s => _rowsByScene.ContainsKey(s.Id))
        .SelectMany(s => _rowsByScene[s.Id])
        .ToList();

    private static bool TryParseFrames(string text, out long frames) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out frames);

    private (VideoClip Clip, ImageInfo Image)? ResolveRow(ClipRow row)
    {
        var clip = row.ShotId is null
            ? _timeline.Scenes
                .SelectMany(s => s.Clips)
                .FirstOrDefault(c =>
                    string.Equals(c.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase))
            : _timeline.Scenes
                .SelectMany(s => s.Clips)
                .FirstOrDefault(c => string.Equals(c.ShotId, row.ShotId, StringComparison.Ordinal));
        var image = _inventory.AllImages.FirstOrDefault(i =>
            string.Equals(i.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase));
        return clip is not null && image is not null && image.Width > 0
            ? (clip, image)
            : null;
    }

    private VideoClip? TimelinePreviousClip(VideoClip clip)
    {
        VideoClip? previous = null;
        foreach (var scene in _timeline.Scenes)
        {
            foreach (var candidate in scene.Clips)
            {
                if (ReferenceEquals(candidate, clip))
                {
                    return previous;
                }

                previous = candidate;
            }
        }

        return null;
    }

    private TransitionPreview? ResolveTransitionPreview(ClipRow row, VideoClip clip, long previewDurationFrames)
    {
        if (!_options.Transitions.Enabled || row.IsFirstClipOfVideo)
        {
            return null;
        }

        var previous = TimelinePreviousClip(clip);
        if (previous is null)
        {
            return null;
        }

        var previousImage = _inventory.AllImages.FirstOrDefault(i =>
            string.Equals(i.FilePath, previous.FilePath, StringComparison.OrdinalIgnoreCase));
        if (previousImage is not { Width: > 0 })
        {
            return null;
        }

        var isSceneBoundary = !string.Equals(previous.SceneId, clip.SceneId, StringComparison.OrdinalIgnoreCase);
        var kind = clip.ShotId is not null
            ? clip.Transition?.Kind ?? TransitionKind.None
            : row.Transition != "Auto" && TransitionCatalog.TryFromName(row.Transition, out var overridden)
                ? overridden
                : isSceneBoundary
                    ? _options.Transitions.SceneBoundaryKind
                    : _options.Transitions.Kind;
        var frames = (long)Math.Round(_options.Transitions.DurationSeconds * _options.Output.Fps);
        var headTrim = PreviewRenderPlanFactory.HeadTrimFrames(frames, _options);
        if (kind == TransitionKind.None || frames < 2 || previewDurationFrames - headTrim < 1)
        {
            return null;
        }

        return new TransitionPreview(
            new TransitionIn { Kind = kind, DurationFrames = frames }, previous, previousImage);
    }

    private Timeline BuildPreviewTimeline(string sceneId, IReadOnlyList<VideoClip> clips) =>
        new()
        {
            Fps = _options.Output.Fps,
            Resolution = new Resolution(_options.Output.Width, _options.Output.Height),
            Audio = new AudioTrack { FilePath = _inventory.AudioFilePath ?? string.Empty },
            Scenes = [new Scene { Id = sceneId, Clips = [.. clips] }],
        };

    private static async Task<bool> TryPromoteAsync(string tempFile, string targetFile)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                }

                File.Move(tempFile, targetFile);
                return true;
            }
            catch (IOException)
            {
                if (attempt == 3)
                {
                    return false;
                }

                if (attempt == 2)
                {
                    // MediaElement releases its media file handle asynchronously; a
                    // forced collection nudges any pending finalizer along.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                await Task.Delay(250);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == 3)
                {
                    return false;
                }

                if (attempt == 2)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                await Task.Delay(250);
            }
        }

        return false;
    }

    private async Task<PreviewRenderOutcome> RenderMiniTimelineAsync(
        Timeline mini, string outputDirectory, string outputPath, double muxOffsetSeconds,
        IProgress<double>? progress)
    {
        Directory.CreateDirectory(outputDirectory);
        var plan = PreviewRenderPlanFactory.Build(
            mini, _inventory.AllImages, _options, outputDirectory, outputPath);
        TryDelete(plan.RoughPath);

        var muxTarget = Path.ChangeExtension(outputPath, ".muxing.mp4");
        TryDelete(muxTarget);
        if (_inventory.AudioFilePath is { } audio)
        {
            plan = plan with
            {
                MuxArguments = PreviewRenderPlanFactory.BuildClipPreviewMuxArguments(
                    plan.RoughPath, audio, muxOffsetSeconds,
                    plan.TotalFrames / _options.Output.Fps, muxTarget),
            };
        }
        else
        {
            plan = plan with { MuxArguments = ["-hide_banner", "-version"] };
        }

        var service = new PreviewRenderService(new FfmpegRunner(_options.Render.FfmpegPath));
        var result = await service.RenderAsync(plan, maxParallelism: 2, progress);
        if (result.Success)
        {
            if (await TryPromoteAsync(muxTarget, outputPath))
            {
                return new PreviewRenderOutcome(outputPath, false, null);
            }

            return new PreviewRenderOutcome(muxTarget, false, null,
                $"{outputPath} was locked — playing the fresh copy from {muxTarget}.");
        }

        TryDelete(muxTarget);
        var errors = string.Join(" | ", result.Errors);
        if (File.Exists(plan.RoughPath))
        {
            return new PreviewRenderOutcome(
                plan.RoughPath, errors.Contains("Audio mux failed", StringComparison.Ordinal), errors);
        }

        return new PreviewRenderOutcome(null, false, errors);
    }

    private async Task<PreviewRenderOutcome> RenderSingleClipPreviewAsync(
        VideoClip previewClip, ImageInfo image, string videoPath, string renderPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(renderPath)!);
        var runner = new FfmpegRunner(_options.Render.FfmpegPath);
        var video = await runner.RunAsync(PreviewRenderPlanFactory.BuildClipPreviewArguments(
            previewClip, image.Width, image.Height, _options, renderPath));
        if (!video.Success)
        {
            return new PreviewRenderOutcome(null, false, video.ErrorTail);
        }

        var muxTarget = Path.ChangeExtension(renderPath, ".muxing.mp4");
        TryDelete(muxTarget);
        if (_inventory.AudioFilePath is { } audio)
        {
            var muxed = await runner.RunAsync(PreviewRenderPlanFactory.BuildClipPreviewMuxArguments(
                renderPath, audio,
                previewClip.StartFrame / _options.Output.Fps,
                previewClip.DurationFrames / _options.Output.Fps,
                muxTarget));
            if (!muxed.Success)
            {
                return new PreviewRenderOutcome(renderPath, true, muxed.ErrorTail);
            }

            TryDelete(renderPath);
            if (await TryPromoteAsync(muxTarget, videoPath))
            {
                return new PreviewRenderOutcome(videoPath, false, null);
            }

            return new PreviewRenderOutcome(muxTarget, false, null,
                $"{videoPath} was locked — playing the fresh copy from {muxTarget}.");
        }

        if (await TryPromoteAsync(renderPath, videoPath))
        {
            return new PreviewRenderOutcome(videoPath, false, null);
        }

        return new PreviewRenderOutcome(renderPath, false, null,
            $"{videoPath} was locked — playing the fresh copy from {renderPath}.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CmbScene_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClipList.ItemsSource = CurrentRows();
        RefreshStrip();
    }

    private void ChkAllScenes_Changed(object sender, RoutedEventArgs e)
    {
        ClipList.ItemsSource = CurrentRows();
        RefreshStrip();
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

    private void NudgeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingNudge || (sender as Slider)?.Tag is not ClipRow row)
        {
            return;
        }

        var slider = (Slider)sender;
        var flat = FlatRows();
        var index = flat.IndexOf(row);
        var next = index >= 0 && index < flat.Count - 1 ? flat[index + 1] : null;

        if (!row.CanNudge || next is null ||
            !TryParseFrames(row.Duration, out var duration) || duration < 1 ||
            !TryParseFrames(next.Duration, out var nextDuration) || nextDuration < 1)
        {
            RestoreNudge(slider, row);
            return;
        }

        var previous = row.Nudge;
        var target = Math.Clamp(e.NewValue, -MaxNudgeFrames, MaxNudgeFrames);
        var delta = (long)Math.Round(target - previous);
        delta = Math.Max(delta, 1 - duration);
        delta = Math.Min(delta, nextDuration - 1);

        // While the user is dragging, leave the thumb where they hold it and
        // apply only the clamped delta — snapping it back mid-drag feels jumpy.
        // DragCompleted syncs the thumb to the applied value.
        if (!_nudgeDragging)
        {
            _updatingNudge = true;
            slider.Value = previous + delta;
            _updatingNudge = false;
        }

        if (delta == 0)
        {
            return;
        }

        row.Nudge = previous + delta;
        row.Duration = (duration + delta).ToString(CultureInfo.InvariantCulture);
        next.Duration = (nextDuration - delta).ToString(CultureInfo.InvariantCulture);
        row.NudgeLabel = NudgeLabelFor(row);
        next.NudgeLabel = NudgeLabelFor(next);
    }

    private void NudgeSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        _nudgeDragging = true;
    }

    private void NudgeSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _nudgeDragging = false;
        if ((sender as Slider)?.Tag is ClipRow row)
        {
            _updatingNudge = true;
            var slider = (Slider)sender;
            slider.Value = Math.Clamp(row.Nudge, -MaxNudgeFrames, MaxNudgeFrames);
            _updatingNudge = false;
        }
    }

    private string NudgeLabelFor(ClipRow row)
    {
        if (!row.CanNudge)
        {
            return string.Empty;
        }

        var duration = TryParseFrames(row.Duration, out var parsed) && parsed > 0
            ? parsed
            : row.PlannedDuration;
        var seconds = (row.StartFrame + duration) / row.Fps;
        return $"{row.Nudge:+0;-0;±0} · cut @ {FormatTimecode(seconds)}";
    }

    private static string FormatTimecode(double seconds) =>
        $"{(int)(seconds / 60)}:{seconds % 60:00.0}";

    private void RestoreNudge(Slider slider, ClipRow row)
    {
        _updatingNudge = true;
        slider.Value = row.Nudge;
        _updatingNudge = false;
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

        // Walk scenes in timeline order so adjacent scenes can pair their boundary
        // shifts (the timing engine accepts opposite full-set deltas across one cut).
        var sceneData = new List<(Scene Scene, IReadOnlyList<ClipRow> Rows, long?[] Durations, bool AllParsed)>();
        var deltas = new long?[_timeline.Scenes.Count];
        for (var index = 0; index < _timeline.Scenes.Count; index++)
        {
            var scene = _timeline.Scenes[index];
            if (!_rowsByScene.TryGetValue(scene.Id, out var rows))
            {
                deltas[index] = null;
                sceneData.Add((scene, [], [], false));
                continue;
            }

            var durations = rows
                .Select(r => TryParseFrames(r.Duration, out var frames) && frames > 0 ? frames : (long?)null)
                .ToArray();

            long emittedTotal = 0;
            var allParsed = true;
            foreach (var frames in durations)
            {
                if (frames is { } value)
                {
                    emittedTotal += value;
                }
                else
                {
                    allParsed = false;
                    break;
                }
            }

            deltas[index] = allParsed ? emittedTotal - (scene.EndFrame - scene.StartFrame) : null;
            sceneData.Add((scene, rows, durations, allParsed));
        }

        for (var index = 0; index + 1 < sceneData.Count; index++)
        {
            var delta = deltas[index];
            var nextDelta = deltas[index + 1];
            if (delta is null || nextDelta is null || delta == 0 || nextDelta != -delta)
            {
                continue;
            }

            deltas[index] = 0;
            deltas[index + 1] = 0;
        }

        for (var index = 0; index < sceneData.Count; index++)
        {
            var (scene, rows, durations, allParsed) = sceneData[index];
            var touched = rows.Any(r => r.Nudge != 0) ||
                          rows.Where((r, i) => durations[i] is { } d && d != r.PlannedDuration).Any();
            var fullSet = allParsed && deltas[index] == 0 && touched;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var clipOverride = new ClipOverride { File = row.FilePath };
                var changed = false;

                if (row.ShotId is not null)
                {
                    // Manifest shots: duration/motion/easing/exclude overrides are
                    // keyed by shot id; the manifest owns order, framing and cuts.
                    clipOverride.Shot = row.ShotId;
                    if (row.Exclude)
                    {
                        clipOverride.Exclude = true;
                        changed = true;
                    }

                    if (row.Motion != "Auto" && MotionCodes.TryFromSuffix(row.Motion, out var shotMotion))
                    {
                        clipOverride.Motion = shotMotion;
                        changed = true;
                    }

                    if (row.Easing != "Auto" && EasingModes.TryFromName(row.Easing, out var shotEasing))
                    {
                        clipOverride.Easing = shotEasing;
                        changed = true;
                    }

                    if (durations[i] is { } shotDuration && shotDuration != row.PlannedDuration)
                    {
                        clipOverride.DurationFrames = shotDuration;
                        changed = true;
                    }

                    if (changed)
                    {
                        result.Clips.Add(clipOverride);
                    }

                    continue;
                }

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

                long? durationOverride;
                if (fullSet)
                {
                    durationOverride = durations[i];
                }
                else if (durations[i] is { } parsed && parsed != row.PlannedDuration)
                {
                    durationOverride = parsed;
                }
                else
                {
                    durationOverride = null;
                }

                if (durationOverride is { } overrideFrames)
                {
                    clipOverride.DurationFrames = overrideFrames;
                    changed = true;
                }

                var naturalIndex = scene.Clips.FindIndex(
                    c => string.Equals(c.FilePath, row.FilePath, StringComparison.OrdinalIgnoreCase));
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

        if (ResolveRow(row) is not { } resolved)
        {
            TxtEditorStatus.Text = "Cannot preview this clip (missing image or dimensions).";
            return;
        }

        var (clip, image) = resolved;
        previewButton.IsEnabled = false;
        TxtEditorStatus.Text = "Rendering clip…";
        ReleasePreviewFiles();

        try
        {
            var previewClip = BuildPreviewClip(row, clip, image.Width, image.Height);
            var clipsDirectory = Path.Combine(_projectFolder, "out", "clips");
            var clipName = Path.GetFileNameWithoutExtension(clip.FilePath);
            var videoPath = Path.Combine(clipsDirectory, clipName + ".mp4");
            var partsDirectory = Path.Combine(clipsDirectory, "parts");

            PreviewRenderOutcome outcome;
            var title = row.Display;
            List<VideoClip> overlayClips;
            var transition = ResolveTransitionPreview(row, clip, previewClip.DurationFrames);
            if (transition is { } transitionPreview)
            {
                var tin = transitionPreview.Transition;
                var tailTrim = PreviewRenderPlanFactory.TailTrimFrames(tin.DurationFrames, _options);
                var headTrim = PreviewRenderPlanFactory.HeadTrimFrames(tin.DurationFrames, _options);
                var shortened = new VideoClip
                {
                    FilePath = transitionPreview.Outgoing.FilePath,
                    SceneId = transitionPreview.Outgoing.SceneId,
                    StartFrame = 0,
                    DurationFrames = tailTrim + tin.DurationFrames,
                    Motion = transitionPreview.Outgoing.Motion,
                    MotionSource = transitionPreview.Outgoing.MotionSource,
                    ImageType = transitionPreview.Outgoing.ImageType,
                    Easing = transitionPreview.Outgoing.Easing,
                    StartViewport = transitionPreview.Outgoing.StartViewport,
                    EndViewport = transitionPreview.Outgoing.EndViewport,
                };
                previewClip.Transition = tin;
                var mini = BuildPreviewTimeline(clip.SceneId, [shortened, previewClip]);
                overlayClips = [shortened, previewClip];
                var offset = Math.Max(0, clip.StartFrame - tailTrim) / _options.Output.Fps;
                var progress = new Progress<double>(p =>
                    TxtEditorStatus.Text = $"Rendering clip… {p * 100:F0}%");
                outcome = await RenderMiniTimelineAsync(mini, partsDirectory, videoPath, offset, progress);
                title = $"Transition into {row.Display}";
            }
            else
            {
                var renderPath = Path.Combine(partsDirectory, clipName + ".render.mp4");
                outcome = await RenderSingleClipPreviewAsync(previewClip, image, videoPath, renderPath);
                overlayClips = [previewClip];
            }

            if (outcome.PlayPath is null)
            {
                TxtEditorStatus.Text = "Preview failed: " + outcome.ErrorTail;
                return;
            }

            if (outcome.MuxFailed)
            {
                MessageBox.Show(this,
                    "The narration could not be mixed into this preview:\n\n" + outcome.ErrorTail +
                    "\n\nThe video-only preview will open instead.",
                    "Preview audio mux failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            TxtEditorStatus.Text = "Clip preview ready." +
                (outcome.Note is null ? "" : " " + outcome.Note);
            _overlaySceneId = null;
            SetOverlayCues(overlayClips);
            PlayerHost.Load(outcome.PlayPath, title);
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

    private async void BtnPlayScene_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ClipRow> rows;
        string sceneId;
        string title;
        if (ShowAllScenes)
        {
            if (CurrentRows() is not { } allRows || allRows.Count == 0)
            {
                return;
            }

            rows = allRows;
            sceneId = "all";
            title = "All scenes";
        }
        else
        {
            if (CmbScene.SelectedValue is not string id || !_rowsByScene.TryGetValue(id, out var sceneRows))
            {
                return;
            }

            rows = sceneRows;
            sceneId = id;
            title = $"Scene {sceneId}";
        }

        var previewClips = new List<VideoClip>();
        var startFrame = 0L;
        var transitionFrames = (long)Math.Round(_options.Transitions.DurationSeconds * _options.Output.Fps);
        foreach (var row in rows)
        {
            if (row.Exclude)
            {
                continue;
            }

            if (ResolveRow(row) is not { } resolved)
            {
                TxtEditorStatus.Text = "Cannot preview this scene (missing image or dimensions).";
                return;
            }

            var (clip, image) = resolved;
            var previewClip = BuildPreviewClip(row, clip, image.Width, image.Height);
            if (previewClips.Count > 0 && _options.Transitions.Enabled &&
                transitionFrames >= 2 && previewClip.Transition is null)
            {
                var kind = row.Transition != "Auto" &&
                           TransitionCatalog.TryFromName(row.Transition, out var overridden)
                    ? overridden
                    : row.Transition == "Cut" ? TransitionKind.None
                    : _options.Transitions.Kind;
                if (kind != TransitionKind.None)
                {
                    previewClip.Transition = new TransitionIn { Kind = kind, DurationFrames = transitionFrames };
                }
            }

            if (previewClips.Count == 0)
            {
                startFrame = previewClip.StartFrame;
            }

            previewClips.Add(previewClip);
        }

        if (previewClips.Count == 0)
        {
            TxtEditorStatus.Text = "Every clip in this scene is excluded — nothing to play.";
            return;
        }

        SetTransportBusy(true);
        TxtEditorStatus.Text = $"Rendering scene {sceneId}…";
        ReleasePreviewFiles();

        try
        {
            var mini = BuildPreviewTimeline(sceneId, previewClips);
            var clipsDirectory = Path.Combine(_projectFolder, "out", "clips");
            var scenePath = Path.Combine(clipsDirectory, $"scene_{sceneId}.mp4");
            var sceneDirectory = Path.Combine(clipsDirectory, $"scene_{sceneId}");
            var progress = new Progress<double>(p =>
                TxtEditorStatus.Text = $"Rendering scene {sceneId}… {p * 100:F0}%");
            var outcome = await RenderMiniTimelineAsync(
                mini, sceneDirectory, scenePath, startFrame / _options.Output.Fps, progress);

            if (outcome.PlayPath is null)
            {
                TxtEditorStatus.Text = "Scene preview failed: " + outcome.ErrorTail;
                return;
            }

            if (outcome.MuxFailed)
            {
                MessageBox.Show(this,
                    "The narration could not be mixed into this preview:\n\n" + outcome.ErrorTail +
                    "\n\nThe video-only preview will open instead.",
                    "Preview audio mux failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            TxtEditorStatus.Text = $"Preview ready ({title})." +
                (outcome.Note is null ? "" : " " + outcome.Note);
            _overlaySceneId = sceneId;
            SetOverlayCues(previewClips);
            PlayerHost.Load(outcome.PlayPath, title);
        }
        catch (Exception ex)
        {
            TxtEditorStatus.Text = "Scene preview failed: " + ex.Message;
        }
        finally
        {
            SetTransportBusy(false);
        }
    }

    private void SetTransportBusy(bool busy)
    {
        BtnPlayScene.IsEnabled = !busy;
        BtnPlayAll.IsEnabled = !busy;
        BtnUndock.IsEnabled = !busy;
    }

    private void BtnPlayAll_Click(object sender, RoutedEventArgs e)
    {
        var previewPath = Path.Combine(_projectFolder, "out", "preview.mp4");
        if (!File.Exists(previewPath))
        {
            TxtEditorStatus.Text = "No full preview found — run BUILD PREVIEW first, then Play all.";
            return;
        }

        ReleasePreviewFiles();
        _overlaySceneId = null;
        SetOverlayCues(_timeline.Scenes.SelectMany(s => s.Clips).ToList());
        PlayerHost.Load(previewPath, "Full preview");
        PlayerHost.ShowNote("Reflects saved overrides only — rebuild after editing overrides.");
        TxtEditorStatus.Text = "Playing the full preview (saved overrides only).";
    }

    private void BtnUndock_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerHost.CurrentPath is null)
        {
            TxtEditorStatus.Text = "Nothing to undock — render a clip preview or use Play scene / Play all first.";
            return;
        }

        var path = PlayerHost.CurrentPath;
        var title = PlayerHost.CurrentTitle;
        PlayerHost.StopAndRelease();

        var floating = new ClipPreviewPlayerWindow(title, path) { Owner = this };
        floating.Closed += FloatingPlayer_Closed;
        _floatingPlayer = floating;
        floating.Show();
        TxtEditorStatus.Text = "Preview undocked — close the floating window to re-dock it.";
    }

    private void FloatingPlayer_Closed(object? sender, EventArgs e)
    {
        _floatingPlayer = null;
        if (sender is ClipPreviewPlayerWindow floating)
        {
            floating.Closed -= FloatingPlayer_Closed;
            _overlaySceneId = null;
            PlayerHost.Load(floating.ClipPath, floating.ClipTitle, autoPlay: false);
        }
    }

    private void ReleasePreviewFiles()
    {
        if (_floatingPlayer is { } floating)
        {
            _floatingPlayer = null;
            floating.Closed -= FloatingPlayer_Closed;
            floating.Close();
        }

        PlayerHost.StopAndRelease();
    }

    private VideoClip BuildPreviewClip(ClipRow row, VideoClip clip, int imageWidth, int imageHeight)
    {
        if (clip.ShotId is not null)
        {
            // Manifest shot: framing, motion and motion duration come from the
            // manifest-planned clip; only the (possibly nudged) duration applies.
            var shotFrames = TryParseFrames(row.Duration, out var parsed) && parsed > 0
                ? parsed
                : clip.DurationFrames;
            return new VideoClip
            {
                FilePath = clip.FilePath,
                ShotId = clip.ShotId,
                SceneId = clip.SceneId,
                StartFrame = clip.StartFrame,
                DurationFrames = shotFrames,
                Motion = clip.Motion,
                MotionSource = clip.MotionSource,
                ImageType = clip.ImageType,
                Easing = clip.Easing,
                StartViewport = clip.StartViewport,
                EndViewport = clip.EndViewport,
                MotionDurationFrames = clip.MotionDurationFrames is { } cap
                    ? Math.Min(cap, Math.Max(2, shotFrames - 1))
                    : null,
                Transition = clip.Transition,
            };
        }

        var motion = row.Motion != "Auto" && MotionCodes.TryFromSuffix(row.Motion, out var overridden)
            ? overridden
            : clip.Motion;
        var easing = row.Easing != "Auto" && EasingModes.TryFromName(row.Easing, out var eased)
            ? eased
            : clip.Easing;
        var duration = TryParseFrames(row.Duration, out var frames) && frames > 0
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
