namespace ImgToVideo.Core.Models;

public sealed record ImageInfo(string FilePath, ParsedImageName Name, int Width, int Height);
