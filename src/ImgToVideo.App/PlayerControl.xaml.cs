using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ImgToVideo.App;

public partial class PlayerControl : UserControl
{
    private readonly DispatcherTimer _timer;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _suppressPositionEvents;

    public event EventHandler<Exception>? PlaybackFailed;

    public string? CurrentPath { get; private set; }
    public string CurrentTitle { get; private set; } = string.Empty;

    public PlayerControl()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => UpdatePositionUi();

        Loaded += (_, _) =>
        {
            if (CurrentPath is not null)
            {
                PlayFromStart();
            }
        };
    }

    public void Load(string path, string title)
    {
        CurrentPath = path;
        CurrentTitle = title;
        TxtTitle.Text = title;
        TxtSubtitle.Text = path;
        BtnPlayPause.IsEnabled = true;
        BtnRestart.IsEnabled = true;
        SldPosition.IsEnabled = true;
        _suppressPositionEvents = true;
        SldPosition.Value = 0;
        _suppressPositionEvents = false;
        TxtTime.Text = "0:00 / 0:00";
        Player.Source = new Uri(path);
        PlayFromStart();
    }

    public void ShowNote(string text) => TxtSubtitle.Text = text;

    public void StopAndRelease()
    {
        _timer.Stop();
        _isSeeking = false;
        try
        {
            Player.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        Player.Close();
        Player.Source = null;
        _isPlaying = false;
        BtnPlayPause.Content = "Play";
        BtnPlayPause.IsEnabled = false;
        BtnRestart.IsEnabled = false;
        SldPosition.IsEnabled = false;
        _suppressPositionEvents = true;
        SldPosition.Value = 0;
        _suppressPositionEvents = false;
        TxtTime.Text = "0:00 / 0:00";
        TxtTitle.Text = "No preview loaded";
        TxtSubtitle.Text = string.Empty;
        CurrentPath = null;
        CurrentTitle = string.Empty;
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        PlayFromStart();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        try
        {
            Player.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        SetPlaying(false);
        _suppressPositionEvents = true;
        SldPosition.Value = 0;
        _suppressPositionEvents = false;
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _timer.Stop();
        _isPlaying = false;
        BtnPlayPause.Content = "Play";
        BtnPlayPause.IsEnabled = false;
        BtnRestart.IsEnabled = false;
        SldPosition.IsEnabled = false;
        TxtSubtitle.Text = "Playback failed: " + e.ErrorException.Message;
        PlaybackFailed?.Invoke(this, e.ErrorException);
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            Player.Pause();
            SetPlaying(false);
        }
        else
        {
            ResumePlay();
        }
    }

    private void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        PlayFromStart();
    }

    private void SldPosition_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isSeeking = true;
    }

    private void SldPosition_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isSeeking = false;
        SeekTo(SldPosition.Value);
        UpdatePositionUi();
    }

    private void SldPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressPositionEvents)
        {
            return;
        }

        if (_isSeeking)
        {
            UpdateTimeLabel(TimeSpan.FromSeconds(Math.Max(0, e.NewValue)));
            return;
        }

        SeekTo(e.NewValue);
    }

    private void SeekTo(double seconds)
    {
        if (Player.NaturalDuration is { HasTimeSpan: true })
        {
            try
            {
                Player.Position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private void PlayFromStart()
    {
        try
        {
            Player.Position = TimeSpan.Zero;
            Player.Play();
            SetPlaying(true);
            _timer.Start();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ResumePlay()
    {
        try
        {
            Player.Play();
            SetPlaying(true);
            _timer.Start();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        BtnPlayPause.Content = playing ? "Pause" : "Play";
    }

    private void UpdatePositionUi()
    {
        if (Player.NaturalDuration is not { HasTimeSpan: true } duration)
        {
            return;
        }

        var total = duration.TimeSpan;
        SldPosition.Maximum = Math.Max(1.0, total.TotalSeconds);
        if (!_isSeeking)
        {
            _suppressPositionEvents = true;
            SldPosition.Value = Math.Min(Player.Position.TotalSeconds, SldPosition.Maximum);
            _suppressPositionEvents = false;
            UpdateTimeLabel(Player.Position);
        }
    }

    private void UpdateTimeLabel(TimeSpan current)
    {
        if (Player.NaturalDuration is { HasTimeSpan: true } duration)
        {
            TxtTime.Text = $"{Format(current)} / {Format(duration.TimeSpan)}";
        }
    }

    private static string Format(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:00}";
}
