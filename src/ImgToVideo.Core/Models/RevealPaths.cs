namespace ImgToVideo.Core.Models;

public static class RevealPaths
{
    /// <summary>
    /// The still showing the first <paramref name="k"/> of <paramref name="count"/>
    /// slices of <paramref name="sourcePath"/>. The last step shows the whole
    /// source image and needs no file of its own. Generated files live next to
    /// the images folder, in out\reveal.
    /// </summary>
    public static string StepPath(string sourcePath, int k, int count)
    {
        if (k >= count)
        {
            return sourcePath;
        }

        var full = Path.GetFullPath(sourcePath);
        var imagesDir = Path.GetDirectoryName(full) ?? ".";
        var projectDir = Path.GetDirectoryName(imagesDir) ?? imagesDir;
        var stem = Path.GetFileNameWithoutExtension(full);
        return Path.Combine(projectDir, "out", "reveal", $"{stem}_reveal{k}of{count}.png");
    }
}
