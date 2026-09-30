using System.Diagnostics;
using System.Text;

namespace ImgToVideo.Ffmpeg;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string ErrorTail
    {
        get
        {
            var trimmed = StandardError.TrimEnd();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            var lines = trimmed.Split('\n');
            return lines.Length <= 40 ? trimmed : string.Join("\n", lines[^40..]);
        }
    }
}

public sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;

    public FfmpegRunner(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ToolLocator.Resolve("ffmpeg", ffmpegPath);
    }

    public async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(e.Data);
            }

            onStandardOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (error)
            {
                error.AppendLine(e.Data);
            }

            onStandardErrorLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new InvalidOperationException(
                $"Failed to start ffmpeg (\"{_ffmpegPath}\"): {e.Message}. " +
                "Install ffmpeg or set the ffmpeg path in the render settings.", e);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            // ConfigureAwait(false) matters here: EncoderProbe.Load calls this
            // method synchronously via ".GetAwaiter().GetResult()" (it has to,
            // since ResolveEncoder/PreviewRenderPlanFactory.Build are public
            // synchronous APIs used by the CLI as well as the UI). When that
            // blocking call happens on the WPF UI thread - which it does, the
            // very first time BUILD PREVIEW/RENDER FINAL runs
            // PreviewRenderPlanFactory.Build() directly on the UI thread before
            // any segment work is parallelized - the awaited continuation below
            // would otherwise be posted back to the UI thread's
            // DispatcherSynchronizationContext to resume. But that thread is
            // blocked inside GetResult() waiting for exactly this task, and a
            // blocked thread can't pump the dispatcher queue that would let its
            // own continuation run: a classic sync-over-async deadlock, and
            // exactly why the app would start "Building..." and then freeze
            // solid - not slow, permanently stuck - the moment it first tried to
            // probe for a GPU encoder. ConfigureAwait(false) resumes this method
            // on a thread-pool thread instead, so the probe still briefly blocks
            // the UI thread while ffmpeg -encoders runs (well under a second),
            // but it always completes instead of hanging forever.
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        process.WaitForExit();

        lock (output)
        {
            lock (error)
            {
                return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
