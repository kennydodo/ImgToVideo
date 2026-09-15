using System.Text;
using System.Text.Json;

namespace ImgToVideo.ImageGen;

public sealed record RenderlyGeneration(
    int Id, string Name, string Status, string? ImageUrl, string? Error);

/// <summary>Client for a running Renderly backend (generation + GPU upscale + file download).</summary>
public sealed class RenderlyClient(string baseUrl)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly string _base = baseUrl.TrimEnd('/');

    private string Api(string path) => $"{_base}/api/{path}";

    public async Task<RenderlyGeneration> GenerateAsync(
        int channelId, string prompt, string name, string imageSize, string aspect,
        IReadOnlyList<int> refAssetIds, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            prompt,
            name,
            image_size = imageSize,
            aspect_ratio = aspect,
            ref_strength = "balanced",
            asset_ids = refAssetIds,
        });
        var json = await PostAsync($"channels/{channelId}/generate", payload, ct);
        return ParseGeneration(json);
    }

    public async Task<RenderlyGeneration> UpscaleAsync(int generationId, int scale, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { scale });
        var json = await PostAsync($"generations/{generationId}/upscale", payload, ct);
        return ParseGeneration(json);
    }

    public async Task<byte[]> DownloadAsync(RenderlyGeneration generation, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(generation.ImageUrl))
        {
            throw new ImageGenerationException("generation has no image url");
        }

        return await Http.GetByteArrayAsync($"{_base}{generation.ImageUrl}", ct);
    }

    private async Task<string> PostAsync(string path, string payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Api(path));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ImageGenerationException($"Renderly HTTP {(int)response.StatusCode}: {Truncate(text)}");
        }

        return text;
    }

    private static RenderlyGeneration ParseGeneration(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RenderlyGeneration(
            root.GetProperty("id").GetInt32(),
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            root.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
            root.TryGetProperty("image_url", out var image) && image.ValueKind == JsonValueKind.String
                ? image.GetString()
                : null,
            root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null);
    }

    private static string Truncate(string text)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }
}
