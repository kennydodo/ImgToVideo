using ImgToVideo.Core.Models;

namespace ImgToVideo.Ffmpeg;

public sealed record SegmentCommand(
    int Index,
    string OutputPath,
    IReadOnlyList<string> Arguments,
    long FrameCount);

public sealed record RenderPlan(
    IReadOnlyList<SegmentCommand> Segments,
    string ConcatListContent,
    IReadOnlyList<string> ConcatArguments,
    string RoughPath,
    IReadOnlyList<string> MuxArguments,
    string PreviewPath,
    long TotalFrames);
