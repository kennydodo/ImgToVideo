using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Options;

public sealed class SoundOptions
{
    /// <summary>Which built-in sound plays when a shotlist asks for "pop" (see <see cref="SoundCatalog.PopVariants"/>).
    /// A file named pop.wav / pop.mp3 in the project's sfx folder still wins.</summary>
    public string DefaultPop { get; set; } = SoundCatalog.DefaultPopId;

    /// <summary>Seeds a brand-new project from the user's saved personal default; an unknown or
    /// missing id leaves the built-in default. Returns true when the default was applied.</summary>
    public bool ApplyPersonalDefault(string? savedId)
    {
        if (!SoundCatalog.IsPopVariant(savedId))
        {
            return false;
        }

        DefaultPop = SoundCatalog.PopVariants.First(v => string.Equals(v.Id, savedId, StringComparison.OrdinalIgnoreCase)).Id;
        return true;
    }
}
