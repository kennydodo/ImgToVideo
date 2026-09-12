using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImgToVideo.Ffmpeg;

public sealed record RenderResult(
    bool Success,
    string OutputPath,
    IReadOnlyList<string> Errors);

public sealed class PreviewRenderService
{
    private const string ManifestFileName = "render-manifest.json";

    private readonly FfmpegRunner _runner;

    public PreviewRenderService(FfmpegRunner runner)
    {
        _runner = runner;
    }

    public async Task<RenderResult> RenderAsync(
        RenderPlan plan,
        int maxParallelism = 2,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        bool reuseUnchangedSegments = false)
    {
        var errors = new List<string>();
        long completedFrames = 0;
        var gate = new object();
        var manifestPath = Path.Combine(
            Path.GetDirectoryName(plan.ConcatListPath)!, ManifestFileName);
        var manifest = LoadManifest(manifestPath);
        var fingerprints = new Dictionary<string, string>();
        var skip = new HashSet<string>();

        foreach (var segment in plan.Segments)
        {
            var key = Path.GetFileName(segment.OutputPath);
            var fingerprint = ComputeFingerprint(segment.Arguments);
            fingerprints[key] = fingerprint;
            if (reuseUnchangedSegments &&
                manifest.TryGetValue(key, out var existing) &&
                existing == fingerprint &&
                File.Exists(segment.OutputPath))
            {
                skip.Add(segment.OutputPath);
                completedFrames += segment.FrameCount;
            }
        }

        progress?.Report(plan.TotalFrames == 0
            ? 1.0
            : completedFrames / (double)plan.TotalFrames);

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelism));
        var segmentTasks = plan.Segments
            .Where(segment => !skip.Contains(segment.OutputPath))
            .Select(async segment =>
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

                    lock (gate)
                    {
                        completedFrames += segment.FrameCount;
                        if (result.Success)
                        {
                            manifest[Path.GetFileName(segment.OutputPath)] =
                                fingerprints[Path.GetFileName(segment.OutputPath)];
                        }
                        else
                        {
                            errors.Add($"Segment {segment.Index} failed (exit {result.ExitCode}): {result.ErrorTail}");
                            manifest.Remove(Path.GetFileName(segment.OutputPath));
                        }
                    }

                    progress?.Report(plan.TotalFrames == 0
                        ? 1.0
                        : completedFrames / (double)plan.TotalFrames);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

        await Task.WhenAll(segmentTasks);
        cancellationToken.ThrowIfCancellationRequested();

        SaveManifest(manifestPath, manifest, fingerprints);

        if (errors.Count > 0)
        {
            return new RenderResult(false, plan.PreviewPath, errors);
        }

        File.WriteAllText(plan.ConcatListPath, plan.ConcatListContent);

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

    private static Dictionary<string, string> LoadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var manifest = JsonSerializer.Deserialize<ManifestFile>(
                File.ReadAllText(path), ManifestJson.Options);
            return manifest?.Segments is { Count: > 0 } segments
                ? new Dictionary<string, string>(segments, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void SaveManifest(
        string path, Dictionary<string, string> manifest, Dictionary<string, string> fingerprints)
    {
        try
        {
            var pruned = fingerprints
                .Where(kvp => manifest.TryGetValue(kvp.Key, out var value) && value == kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(
                path, JsonSerializer.Serialize(new ManifestFile(pruned), ManifestJson.Options));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ComputeFingerprint(IReadOnlyList<string> arguments)
    {
        var inputs = new List<string>();
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "-i")
            {
                inputs.Add(arguments[i + 1]);
            }
        }

        var payload = string.Join('\n', arguments) + "\n" + string.Join('\n', inputs.Select(InputStamp));
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    private static string InputStamp(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists
                ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
                : $"{file}|missing";
        }
        catch (IOException)
        {
            return $"{file}|missing";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{file}|missing";
        }
    }

    private sealed record ManifestFile(
        [property: JsonPropertyName("segments")] Dictionary<string, string> Segments);

    private static class ManifestJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
    }
}
