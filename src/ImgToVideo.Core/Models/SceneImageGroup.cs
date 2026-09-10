namespace ImgToVideo.Core.Models;

public sealed record SceneImageGroup(int SceneNumber, string SceneId, IReadOnlyList<ImageInfo> Images);
