namespace ImgToVideo.Core.Models;

public sealed record ScenePlanInput(
    string SceneId,
    double StartSeconds,
    double EndSeconds,
    IReadOnlyList<ImageInfo> Images);
