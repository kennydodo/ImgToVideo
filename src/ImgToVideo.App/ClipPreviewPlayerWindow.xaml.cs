using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ImgToVideo.App;

public partial class ClipPreviewPlayerWindow : Window
{
    private readonly string _clipPath;
    private bool _isPlaying;

    public ClipPreviewPlayerWindow(string clipName, string clipPath)
    {
        InitializeComponent();
        _clipPath = clipPath;
        TxtTitle.Text = clipName;
        TxtSubtitle.Text = clipPath;
        Player.Source = new Uri(clipPath);
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        Play();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        Player.Position = TimeSpan.Zero;
        Play();
    }

    private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        BtnPlayPause.IsEnabled = false;
        TxtSubtitle.Text = "Inline playback failed (" + e.ErrorException.Message + ") — opening the file with your default player instead.";
        TryOpenExternally();
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
            Play();
        }
    }

    private void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        Player.Position = TimeSpan.Zero;
        Play();
    }

    private void Play()
    {
        Player.Play();
        SetPlaying(true);
    }

    private void SetPlaying(bool playing)
    {
        _isPlaying = playing;
        BtnPlayPause.Content = playing ? "Pause" : "Play";
    }

    private void TryOpenExternally()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_clipPath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
