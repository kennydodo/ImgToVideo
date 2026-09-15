using System.Globalization;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;

namespace ImgToVideo.ImageGen;

internal static class Program
{
    private sealed record Options(
        string? ProjectFolder,
        bool Force,
        int Parallel,
        string Model,
        string? Aspect,
        string? ApiKey,
        bool DryRun,
        bool ListModels,
        string? RenderlyBase,
        int? ChannelId,
        string ImageSize,
        int? UpscaleScale,
        IReadOnlyList<int> RefAssetIds);

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = ParseArgs(args);
        }
        catch (Exception ex)
        {
            PrintUsage();
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }

        try
        {
            if (options.ListModels)
            {
                return ListModelsAsync(RequireApiKey(options.ApiKey)).GetAwaiter().GetResult();
            }

            return RunAsync(options).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static string RequireApiKey(string? apiKey) =>
        apiKey
        ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
        ?? throw new InvalidOperationException(
            "no API key — set the GEMINI_API_KEY environment variable or pass --api-key.");

    private static async Task<int> ListModelsAsync(string apiKey)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
        request.Headers.Add("x-goog-api-key", apiKey);
        using var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine("error: HTTP " + (int)response.StatusCode + " — " + body);
            return 2;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            if (!model.TryGetProperty("name", out var nameElement) ||
                nameElement.GetString() is not { } name)
            {
                continue;
            }

            if (name.Contains("image", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(name);
            }
        }

        return 0;
    }

    private static async Task<int> RunAsync(Options options)
    {
        var projectFolder = options.ProjectFolder
            ?? throw new InvalidOperationException("a project folder is required.");
        var shotlistPath = Path.Combine(projectFolder, ShotListParser.FileName);
        if (!File.Exists(shotlistPath))
        {
            throw new FileNotFoundException(
                $"no {ShotListParser.FileName} in {projectFolder} — run the LLM planning step first.");
        }

        var issues = new List<ValidationIssue>();
        var document = ShotListParser.Parse(File.ReadAllText(shotlistPath), issues);
        foreach (var issue in issues.Where(i => i.Severity != ValidationSeverity.Info))
        {
            Console.WriteLine($"[warn] {issue.Message}");
        }

        var imagesDirectory = Path.Combine(projectFolder, "images");
        Directory.CreateDirectory(imagesDirectory);

        var planned = document.Prompts
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (File: kv.Key, Prompt: kv.Value))
            .ToList();
        Console.WriteLine(
            $"shotlist: {document.Shots.Count} shots, {planned.Count} planned images" +
            (document.Style is null ? "" : " + master style prompt"));

        var review = new List<ReviewItem>();
        var reviewGate = new object();
        var toGenerate = new List<(string File, string Prompt)>();
        var skipped = 0;
        foreach (var (file, prompt) in planned)
        {
            if (file != Path.GetFileName(file) || file.Contains(':'))
            {
                review.Add(new ReviewItem(file, prompt, ReviewStatus.Failed,
                    "filename must be a plain file name (no folders)"));
                Console.WriteLine($"[fail] {file} — filename must be a plain file name");
                continue;
            }

            if (string.IsNullOrEmpty(prompt))
            {
                review.Add(new ReviewItem(file, prompt, ReviewStatus.Failed, "no prompt in shotlist.json"));
                Console.WriteLine($"[fail] {file} — no prompt in shotlist.json");
                continue;
            }

            var target = Path.Combine(imagesDirectory, file);
            if (!options.Force && File.Exists(target))
            {
                skipped++;
                review.Add(new ReviewItem(file, prompt, ReviewStatus.Existing, "already on disk"));
                Console.WriteLine($"[skip] {file} (exists)");
                continue;
            }

            toGenerate.Add((file, prompt));
            review.Add(new ReviewItem(file, prompt, ReviewStatus.Generated, string.Empty));
        }

        if (options.DryRun)
        {
            Console.WriteLine($"dry run: {toGenerate.Count} would be generated, {skipped} already exist.");
            foreach (var (file, prompt) in toGenerate)
            {
                Console.WriteLine($"[gen ] {file} ({prompt.Length} chars)");
            }

            return 0;
        }

        if (toGenerate.Count == 0)
        {
            Console.WriteLine("nothing to generate — every planned image is already on disk.");
            WriteContactSheet(projectFolder, review);
            return 0;
        }

        RenderlyClient? renderly = null;
        if (options.RenderlyBase is { } renderlyBase)
        {
            renderly = new RenderlyClient(renderlyBase);
        }
        else
        {
            Console.WriteLine($"backend: Gemini API direct, model {options.Model}");
        }

        var apiKey = renderly is null ? RequireApiKey(options.ApiKey) : null;
        var generated = 0;
        var failed = 0;
        var start = DateTime.Now;

        await Parallel.ForEachAsync(
            toGenerate,
            new ParallelOptions { MaxDegreeOfParallelism = options.Parallel },
            async (item, ct) =>
            {
                try
                {
                    var fullPrompt = document.Style is null
                        ? item.Prompt
                        : document.Style + "\n\n" + item.Prompt;
                    byte[] data;
                    if (renderly is not null)
                    {
                        data = await GenerateViaRenderlyAsync(
                            renderly, options, item, fullPrompt, ct);
                    }
                    else
                    {
                        var client = new GeminiImageClient(options.Model, apiKey!, options.Aspect);
                        (data, _) = await client.GenerateAsync(fullPrompt, ct);
                    }

                    var target = Path.Combine(imagesDirectory, item.File);
                    await File.WriteAllBytesAsync(target, data, ct);

                    lock (reviewGate)
                    {
                        Interlocked.Increment(ref generated);
                        review[review.FindIndex(r => r.FileName == item.File)] = new ReviewItem(
                            item.File, item.Prompt, ReviewStatus.Generated, ImageFile.SizeText(data.Length));
                    }

                    Console.WriteLine(
                        $"[ok]   {item.File} ({ImageFile.SizeText(data.Length)})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (reviewGate)
                    {
                        Interlocked.Increment(ref failed);
                        review[review.FindIndex(r => r.FileName == item.File)] = new ReviewItem(
                            item.File, item.Prompt, ReviewStatus.Failed, ex.Message);
                    }

                    Console.WriteLine($"[fail] {item.File} — {ex.Message}");
                }
            });

        var elapsed = DateTime.Now - start;
        Console.WriteLine(
            $"done in {elapsed.TotalMinutes:0.#} min: {generated} generated, {skipped} skipped, {failed} failed.");

        WriteContactSheet(projectFolder, review);
        Console.WriteLine($"review sheet: {Path.Combine(projectFolder, "out", "image-review.html")}");
        return failed > 0 ? 1 : 0;
    }

    private static async Task<byte[]> GenerateViaRenderlyAsync(
        RenderlyClient renderly, Options options,
        (string File, string Prompt) item, string fullPrompt, CancellationToken ct)
    {
        var name = Path.GetFileNameWithoutExtension(item.File);
        var generation = await renderly.GenerateAsync(
            options.ChannelId!.Value, fullPrompt, name, options.ImageSize,
            options.Aspect ?? "16:9", options.RefAssetIds, ct);
        if (generation.Status != "done" || generation.ImageUrl is null)
        {
            throw new ImageGenerationException(generation.Error is { Length: > 0 } error
                ? error
                : $"generation status {generation.Status}");
        }

        if (options.UpscaleScale is { } scale)
        {
            generation = await renderly.UpscaleAsync(generation.Id, scale, ct);
            if (generation.Status != "done" || generation.ImageUrl is null)
            {
                throw new ImageGenerationException(generation.Error is { Length: > 0 } upscaledError
                    ? upscaledError
                    : $"upscale status {generation.Status}");
            }
        }

        return await renderly.DownloadAsync(generation, ct);
    }

    private static void WriteContactSheet(string projectFolder, IReadOnlyList<ReviewItem> review)
    {
        var outDirectory = Path.Combine(projectFolder, "out");
        Directory.CreateDirectory(outDirectory);
        ContactSheetWriter.Write(
            Path.Combine(outDirectory, "image-review.html"),
            Path.GetFileName(projectFolder.TrimEnd(Path.DirectorySeparatorChar)),
            review);
    }

    private static Options ParseArgs(string[] args)
    {
        string? folder = null;
        bool force = false, dryRun = false, listModels = false;
        int parallel = 2;
        string model = "gemini-2.5-flash-image";
        string? aspect = null, apiKey = null, renderlyBase = null, imageSize = "1K";
        int? channelId = null, upscaleScale = null;
        var refAssets = new List<int>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--force":
                    force = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--list-models":
                    listModels = true;
                    break;
                case "--parallel":
                case "--model":
                case "--aspect":
                case "--api-key":
                case "--renderly":
                case "--channel":
                case "--image-size":
                case "--upscale":
                case "--ref-asset":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{arg} needs a value.");
                    }

                    switch (arg)
                    {
                        case "--parallel":
                            if (!int.TryParse(args[++i], out parallel) || parallel < 1 || parallel > 8)
                            {
                                throw new ArgumentException("--parallel must be between 1 and 8.");
                            }

                            break;
                        case "--model":
                            model = args[++i];
                            break;
                        case "--aspect":
                            aspect = args[++i];
                            break;
                        case "--api-key":
                            apiKey = args[++i];
                            break;
                        case "--renderly":
                            renderlyBase = args[++i].TrimEnd('/');
                            break;
                        case "--channel":
                            if (!int.TryParse(args[++i], out var channel) || channel < 1)
                            {
                                throw new ArgumentException("--channel must be a positive id.");
                            }

                            channelId = channel;
                            break;
                        case "--image-size":
                            imageSize = args[++i].ToUpperInvariant();
                            if (imageSize is not ("1K" or "2K" or "4K"))
                            {
                                throw new ArgumentException("--image-size must be 1K, 2K or 4K.");
                            }

                            break;
                        case "--upscale":
                            if (!int.TryParse(args[++i], out var scale) || scale is not (2 or 4))
                            {
                                throw new ArgumentException("--upscale must be 2 or 4.");
                            }

                            upscaleScale = scale;
                            break;
                        case "--ref-asset":
                            foreach (var token in args[++i].Split(','))
                            {
                                if (!int.TryParse(token.Trim(), out var assetId) || assetId < 1)
                                {
                                    throw new ArgumentException(
                                        $"--ref-asset must be ids like \"12,13\" (got \"{token}\").");
                                }

                                refAssets.Add(assetId);
                            }

                            break;
                    }

                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"unknown option \"{arg}\".");
                    }

                    if (folder is not null)
                    {
                        throw new ArgumentException("only one project folder can be given.");
                    }

                    folder = arg;
                    break;
            }
        }

        if (folder is null && !listModels)
        {
            throw new ArgumentException("a project folder is required.");
        }

        if (renderlyBase is not null && channelId is null)
        {
            throw new ArgumentException("--renderly needs --channel <id> (see RENDERLY).");
        }

        if (upscaleScale is not null && renderlyBase is null)
        {
            throw new ArgumentException("--upscale only works together with --renderly.");
        }

        if (folder is not null)
        {
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder))
            {
                throw new ArgumentException($"project folder does not exist: {folder}");
            }
        }

        return new Options(
            folder, force, parallel, model, aspect, apiKey, dryRun, listModels,
            renderlyBase, channelId, imageSize, upscaleScale, refAssets);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            ImgToVideo batch image generator — turns shotlist.json into images\ in one run.

            usage:
              dotnet run --project src/ImgToVideo.ImageGen -- <projectFolder> [options]

            options:
              --renderly <url>   route generation through a running Renderly backend
                                 (e.g. http://127.0.0.1:8022); needs --channel
              --channel <id>     Renderly channel to generate into
              --image-size <s>   Renderly output size: 1K, 2K or 4K (default 1K)
              --upscale <2|4>    after generation, upscale in Renderly (Real-ESRGAN GPU)
                                 and keep the upscaled file
              --ref-asset <ids>  comma-separated Renderly asset ids used as style/subject
                                 references for every image (character consistency)
              --parallel <1-8>   concurrent generations (default 2; keep low, the API rate-limits)
              --model <name>     Gemini image model (direct mode only; default
                                 gemini-2.5-flash-image; --list-models shows the image models
                                 on your account)
              --aspect <ratio>   aspect hint (default 16:9)
              --api-key <key>    API key for direct mode (default: GEMINI_API_KEY or
                                 GOOGLE_API_KEY env var; Renderly mode uses its own key)
              --force            regenerate even when the file already exists
              --dry-run          list what would be generated without calling the API
              --list-models      print available image model IDs and exit

            Without --renderly the tool calls the Gemini API directly. With --renderly,
            generation runs inside Renderly (history, spend tracking, channel references,
            optional GPU upscale) and the finished image is downloaded into images\.

            The shotlist "style" field is prepended to every prompt. Existing files are
            skipped, so re-running the tool only fills the gaps. A review sheet is written
            to out\image-review.html.
            """);
    }
}
