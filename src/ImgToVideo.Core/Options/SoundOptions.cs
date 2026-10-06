using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class SoundOptions
{
    /// <summary>Which built-in sound plays when a shotlist asks for "pop" (see <see cref="SoundCatalog.PopVariants"/>).
    /// A file named pop.wav / pop.mp3 in the project's sfx folder still wins.</summary>
    public string DefaultPop { get; set; } = SoundCatalog.DefaultPopId;
}
