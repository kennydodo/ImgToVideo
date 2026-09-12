using System.Diagnostics;
using System.Windows;

namespace ImgToVideo.App;

public partial class ClipPreviewPlayerWindow : Window
{
    public string ClipPath { get; }
    public string ClipTitle { get; }

    public ClipPreviewPlayerWindow(string clipName, string clipPath)
    {
        InitializeComponent();
        ClipPath = clipPath;
        ClipTitle = clipName;
        Title = clipName;
        Player.Load(clipPath, clipName);
        Player.PlaybackFailed += (_, ex) =>
        {
            Player.ShowNote("Inline playback failed (" + ex.Message +
                            ") — opening the file with your default player instead.");
            TryOpenExternally();
        };
    }

    private void TryOpenExternally()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ClipPath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
