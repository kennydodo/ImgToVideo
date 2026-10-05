using System.Security.Cryptography;
using System.Text;

namespace ImgToVideo.Core.Models;

public static class RevealPaths
{
    /// <summary>
    /// The still showing the first <paramref name="k"/> of <paramref name="count"/>
    /// items of <paramref name="sourcePath"/>. The last step shows the whole
    /// source image and needs no file of its own. Generated files live next to
    /// the images folder, in out\reveal.
    /// </summary>
    public static string StepPath(string sourcePath, int k, int count, string layout = RevealLayouts.Row)
    {
        if (k >= count)
        {
            return sourcePath;
        }

        var full = Path.GetFullPath(sourcePath);
        var stem = Path.GetFileNameWithoutExtension(full);
        var tag = RevealLayouts.Normalize(layout) == RevealLayouts.Grid ? "grid" : "reveal";
        return Path.Combine(RevealDirectory(full), $"{stem}_{tag}{k}of{count}.png");
    }

    /// <summary>
    /// The composite of the first <paramref name="k"/> of several separate images (each in its slot).
    /// Every step has a file of its own; a short hash of the image list keeps two shots that
    /// start from the same image apart.
    /// </summary>
    public static string StackPath(IReadOnlyList<string> sources, int k, string layout)
    {
        var first = Path.GetFullPath(sources[0]);
        var key = string.Join("|", sources.Select(s => Path.GetFileName(s).ToLowerInvariant())) + "|" +
                  RevealLayouts.Normalize(layout);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..6].ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(first);
        return Path.Combine(RevealDirectory(first), $"{stem}_stack{hash}_{k}of{sources.Count}.png");
    }

    /// <summary>The project folder (parent of the images folder) given any image path.</summary>
    public static string ProjectDirectory(string imagePath)
    {
        var full = Path.GetFullPath(imagePath);
        var imagesDir = Path.GetDirectoryName(full) ?? ".";
        return Path.GetDirectoryName(imagesDir) ?? imagesDir;
    }

    private static string RevealDirectory(string fullImagePath) =>
        Path.Combine(ProjectDirectory(fullImagePath), "out", "reveal");
}
