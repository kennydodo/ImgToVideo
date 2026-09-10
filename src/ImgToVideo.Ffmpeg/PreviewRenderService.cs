namespace ImgToVideo.Ffmpeg;

public sealed record RenderResult(
    bool Success,
    string OutputPath,
    IReadOnlyList<string> Errors);

public sealed class PreviewRenderService
{
    private readonly FfmpegRunner _runner;

    public PreviewRenderService(FfmpegRunner runner)
    {
        _runner = runner;
    }

    public async Task<RenderResult> RenderAsync(
        RenderPlan plan,
        int maxParallelism = 2,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        long completedFrames = 0;
        var gate = new object();

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelism));
        var segmentTasks = plan.Segments.Select(async segment =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await _runner.RunAsync(
                    segment.Arguments,
                    onStandardOutputLine: line =>
                    {
                        if (!line.StartsWith("frame=", StringComparison.Ordinal) ||
                            !long.TryParse(line["frame=".Length..].Trim(), out var frame))
                        {
                            return;
                        }

                        double fraction;
                        lock (gate)
                        {
                            var done = Math.Min(frame, segment.FrameCount);
                            fraction = (completedFrames + done) / (double)plan.TotalFrames;
                        }

                        progress?.Report(fraction);
                    },
                    cancellationToken: cancellationToken);

                if (!result.Success)
                {
                    var message = $"Segment {segment.Index} failed (exit {result.ExitCode}): {result.ErrorTail}";
                    lock (errors)
                    {
                        errors.Add(message);
                    }
                }

                lock (gate)
                {
                    completedFrames += segment.FrameCount;
                }

                progress?.Report(completedFrames / (double)plan.TotalFrames);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(segmentTasks);
        cancellationToken.ThrowIfCancellationRequested();

        if (errors.Count > 0)
        {
            return new RenderResult(false, plan.PreviewPath, errors);
        }

        var concat = await _runner.RunAsync(plan.ConcatArguments, cancellationToken: cancellationToken);
        if (!concat.Success)
        {
            return new RenderResult(false, plan.PreviewPath,
                [$"Concatenation failed (exit {concat.ExitCode}): {concat.ErrorTail}"]);
        }

        var mux = await _runner.RunAsync(plan.MuxArguments, cancellationToken: cancellationToken);
        if (!mux.Success)
        {
            return new RenderResult(false, plan.PreviewPath,
                [$"Audio mux failed (exit {mux.ExitCode}): {mux.ErrorTail}"]);
        }

        progress?.Report(1.0);
        return new RenderResult(true, plan.PreviewPath, []);
    }
}
