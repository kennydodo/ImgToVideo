// WhisperRadar headless driver for the ImgToVideo pipeline.
// Runs the same analyze -> plan -> render sequence as the WPF app, no UI.
//
//   ImgToVideo.Cli render-final <projectFolder> [--preview]
//   ImgToVideo.Cli plan <projectFolder>
//
// render-final produces out\final\final.mp4 (+ captions.srt); --preview
// renders the fast draft to out\preview.mp4 instead.

using System.Text.Json;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Reporting;
using ImgToVideo.Core.Serialization;
using ImgToVideo.Ffmpeg;

if (args.Length < 2)
{
    Console.Error.WriteLine("""
        usage:
          ImgToVideo.Cli render-final <projectFolder> [--preview]
          ImgToVideo.Cli plan <projectFolder>
        """);
    return 2;
}

var command = args[0].ToLowerInvariant();
var folder = Path.GetFullPath(args[1]);
var preview = args.Contains("--preview", StringComparer.OrdinalIgnoreCase);

if (command is not ("render-final" or "plan"))
{
    Console.Error.WriteLine($"unknown command: {command}");
    return 2;
}
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine($"project folder not found: {folder}");
    return 2;
}

var optionsPath = Path.Combine(folder, "imgtovideo.json");
if (!File.Exists(optionsPath))
    OptionsJson.Save(new ProjectOptions(), optionsPath);
var options = OptionsJson.LoadOrDefault(optionsPath);

var configErrors = options.Validate();
if (configErrors.Count > 0)
{
    Console.Error.WriteLine("configuration errors:");
    foreach (var e in configErrors)
        Console.Error.WriteLine($"  - {e}");
    return 2;
}

var inventory = ProjectLoader.Load(folder, options);
ReportIssues(inventory.Issues);
if (inventory.AudioFilePath is null)
{
    Console.Error.WriteLine("no audio found in project folder");
    return 1;
}

var audioSeconds = await new Ffprobe(options.Render.FfprobePath)
    .GetDurationSecondsAsync(inventory.AudioFilePath);
Console.WriteLine(
    $"project: {inventory.AllImages.Count} image(s), audio {audioSeconds:F1}s");

var planned = EditPlanner.Plan(inventory, audioSeconds, options);
if (planned.Timeline is null || !planned.Success)
{
    Console.Error.WriteLine("planning failed:");
    ReportIssues(planned.Issues);
    return 1;
}
ReportIssues(planned.Issues);

var outDir = Path.Combine(folder, "out");
Directory.CreateDirectory(outDir);
TimelineJson.Save(planned.Timeline, Path.Combine(outDir, "timeline.json"));
BuildReportWriter.Save(
    BuildReportFactory.Create(Path.GetFileName(folder), planned.Timeline,
        options, planned.Issues, planned.Coverage),
    Path.Combine(outDir, "build-report.json"));

if (command == "plan")
{
    Console.WriteLine($"plan ok: {planned.Timeline.Scenes.Count} scene(s)");
    Console.WriteLine($"timeline: {Path.Combine(outDir, "timeline.json")}");
    return 0;
}

var progress = new Progress<double>(p => Console.Write($"\rrender {p,5:P1}   "));
var service = new PreviewRenderService(new FfmpegRunner(options.Render.FfmpegPath));
RenderResult result;
if (preview)
{
    var plan = PreviewRenderPlanFactory.Build(
        planned.Timeline, inventory.AllImages, options,
        Path.Combine(outDir, "render"), Path.Combine(outDir, "preview.mp4"));
    result = await service.RenderAsync(plan, maxParallelism: 2, progress);
}
else
{
    var renderOptions = FinalRenderOptions(options);
    var finalDirectory = Path.Combine(outDir, "final");
    var plan = PreviewRenderPlanFactory.Build(
        planned.Timeline, inventory.AllImages, renderOptions,
        Path.Combine(finalDirectory, "render"),
        Path.Combine(finalDirectory, "final.mp4"));
    result = await service.RenderAsync(plan, maxParallelism: 2, progress);
    if (result.Success && inventory.SrtFilePath is not null)
        File.Copy(inventory.SrtFilePath,
            Path.Combine(finalDirectory, "captions.srt"), overwrite: true);
}
Console.WriteLine();
if (!result.Success)
{
    Console.Error.WriteLine("render failed:");
    foreach (var e in result.Errors)
        Console.Error.WriteLine($"  {e}");
    return 1;
}

Console.WriteLine($"output: {result.OutputPath}");
return 0;

static ProjectOptions FinalRenderOptions(ProjectOptions options)
{
    // same clone the WPF app does: render at project resolution with the
    // final preset/CRF
    var clone = OptionsJson.LoadFromJson(
        JsonSerializer.Serialize(options, OptionsJson.JsonOptions));
    clone.Render.PreviewWidth = clone.Output.Width;
    clone.Render.PreviewHeight = clone.Output.Height;
    clone.Render.PreviewPreset = clone.Render.FinalPreset;
    clone.Render.PreviewCrf = clone.Render.FinalCrf;
    return clone;
}

static void ReportIssues(IReadOnlyList<ValidationIssue> issues)
{
    foreach (var issue in issues)
        Console.WriteLine($"  [{issue.Severity}] {issue.Code}: {issue.Message}");
}
