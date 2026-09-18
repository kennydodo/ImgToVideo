// WhisperRadar headless driver for the ImgToVideo pipeline.
// Runs the same analyze -> plan -> render sequence as the WPF app, no UI.
//
//   ImgToVideo.Cli render-final <projectFolder> [--preview]
//   ImgToVideo.Cli plan <projectFolder>
//   ImgToVideo.Cli export-premiere <projectFolder>
//   ImgToVideo.Cli export-capcut <projectFolder>
//   ImgToVideo.Cli export-batch <projectFolder>
//
// render-final produces out\final\final.mp4 (+ captions.srt); --preview
// renders the fast draft to out\preview.mp4 instead. export-premiere writes
// out\premiere.xml (FCP7 XML with motion keyframes); export-capcut writes
// out\capcut\<name>\ (CapCut draft folder — copy into CapCut's draft root).

using System.Text.Json;
using System.Text.Json.Nodes;
using ImgToVideo.CapCut;
using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using ImgToVideo.Core.Reporting;
using ImgToVideo.Core.Serialization;
using ImgToVideo.Ffmpeg;
using ImgToVideo.Premiere;

if (args.Length < 2)
{
    Console.Error.WriteLine("""
        usage:
          ImgToVideo.Cli render-final <projectFolder> [--preview]
          ImgToVideo.Cli plan <projectFolder>
          ImgToVideo.Cli export-premiere <projectFolder>
          ImgToVideo.Cli export-capcut <projectFolder>
        """);
    return 2;
}

var command = args[0].ToLowerInvariant();
var folder = Path.GetFullPath(args[1]);
var preview = args.Contains("--preview", StringComparer.OrdinalIgnoreCase);

if (command is not ("render-final" or "plan" or "export-premiere" or "export-capcut" or "export-batch"))
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

if (command == "export-premiere")
{
    var xmlPath = Path.Combine(outDir, "premiere.xml");
    var xml = Fcp7XmlExporter.Export(
        planned.Timeline, inventory.AllImages,
        new PremiereExportOptions { IncludeMotionKeyframes = true });
    File.WriteAllText(xmlPath, xml);
    Console.WriteLine($"premiere xml: {xmlPath} — import it into Premiere (File > Import).");
    return 0;
}

if (command == "export-capcut")
{
    var draftName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
    var draftFolder = Path.Combine(outDir, "capcut", draftName);
    var contentPath = CapCutDraftExporter.Export(planned.Timeline, inventory.AllImages, draftFolder);
    var draftRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CapCut", "User Data", "Projects", "com.lveditor.draft");
    Console.WriteLine($"capcut draft: {draftFolder}");
    Console.WriteLine($"copy the folder into {draftRoot} (CapCut closed), then open it in CapCut.");
    Console.WriteLine($"draft content: {contentPath}");
    return 0;
}

if (command == "export-batch")
{
    // The image-generation delta: plan images[] minus what already exists in
    // images\, as a batch JSON (master prompt + canvas per motion code) for
    // Flow/Renderly. Reads the shotlist directly — no render involved.
    var shotlistPath = Path.Combine(folder, "shotlist.json");
    if (!File.Exists(shotlistPath))
    {
        Console.Error.WriteLine("no shotlist.json in the project folder");
        return 1;
    }

    var shotlist = JsonNode.Parse(File.ReadAllText(shotlistPath));
    var imagesDir = Path.Combine(folder, "images");
    var onDisk = Directory.Exists(imagesDir)
        ? Directory.GetFiles(imagesDir, "*.png").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var canvas = new Dictionary<string, string>
    {
        ["ST"] = "2304x1296", ["ZI"] = "2304x1296", ["ZO"] = "2304x1296",
        ["PL"] = "2880x1296", ["PR"] = "2880x1296",
        ["PU"] = "2304x2160", ["PD"] = "2304x2160",
        ["PV"] = "3840x1296",
    };

    var missing = new JsonArray();
    foreach (var img in shotlist["images"]!.AsArray())
    {
        var file = (string?)img!["file"] ?? "";
        if (onDisk.Contains(file))
        {
            continue;
        }

        var motion = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? file[..^4].Split('_')[^1]
            : file.Split('_')[^1];
        missing.Add(new JsonObject
        {
            ["file"] = file,
            ["canvas"] = canvas.GetValueOrDefault(motion, "2304x1296"),
            ["motion"] = motion,
            ["prompt"] = (string?)img["prompt"] ?? "",
        });
    }

    var batch = new JsonObject
    {
        ["note"] = "Missing images only — regenerate into images\\. Master prompt goes in the batch app's master box once.",
        ["master_prompt"] = (string?)shotlist["style"] ?? "",
        ["count"] = missing.Count,
        ["images"] = missing,
    };

    var batchPath = Path.Combine(outDir, "image-batch.json");
    File.WriteAllText(batchPath, batch.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true,
        // prompts are hand-read and hand-pasted: keep em-dashes etc. literal
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"missing images: {missing.Count} of {shotlist["images"]!.AsArray().Count} plan entries");
    Console.WriteLine($"batch json: {batchPath}");
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
