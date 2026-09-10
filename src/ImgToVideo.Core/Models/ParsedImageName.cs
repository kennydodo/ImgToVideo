namespace ImgToVideo.Core.Models;

public sealed record ParsedImageName(
    string Stem,
    int SceneNumber,
    int ImageNumber,
    MotionType? Code,
    bool HasUnknownSuffix,
    ImageType? Type = null);
