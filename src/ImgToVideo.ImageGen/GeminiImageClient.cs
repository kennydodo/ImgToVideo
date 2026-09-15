using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ImgToVideo.ImageGen;

public sealed class ImageGenerationException : Exception
{
    public ImageGenerationException(string message) : base(message)
    {
    }
}

/// <summary>Generates one image per call through the Gemini API (nano banana),
/// retrying transient failures (rate limits, 5xx, network) with backoff.</summary>
public sealed class GeminiImageClient(string model, string apiKey, string? aspect)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(180) };
    private const int MaxAttempts = 4;
    private readonly string _model = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
        ? model["models/".Length..]
        : model;

    public async Task<(byte[] Data, string MimeType)> GenerateAsync(string prompt, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";
        var contents = new[] { new { parts = new[] { new { text = prompt } } } };
        var payload = aspect is null
            ? JsonSerializer.Serialize(new { contents })
            : JsonSerializer.Serialize(new
            {
                contents,
                generationConfig = new { imageConfig = new { aspectRatio = aspect } },
            });

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await GenerateOnceAsync(url, payload, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ImageGenerationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TransientStatusException or TaskCanceledException)
            {
                lastError = ex;
                if (attempt == MaxAttempts)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 4), ct);
            }
        }

        throw new ImageGenerationException(
            $"failed after {MaxAttempts} attempts: {lastError?.Message ?? "unknown error"}");
    }

    private async Task<(byte[] Data, string MimeType)> GenerateOnceAsync(
        string url, string body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ErrorDetail(text);
            // Billing-exhausted 429s are permanent - retrying only wastes time.
            if ((int)response.StatusCode == 429 &&
                (detail.Contains("prepayment", StringComparison.OrdinalIgnoreCase) ||
                 detail.Contains("billing", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ImageGenerationException($"HTTP 429 (billing): {detail}");
            }

            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                throw new TransientStatusException($"HTTP {(int)response.StatusCode}: {detail}");
            }

            throw new ImageGenerationException($"HTTP {(int)response.StatusCode}: {detail}");
        }

        return ParseImageResponse(text);
    }

    private static (byte[] Data, string MimeType) ParseImageResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var context = string.Empty;
        if (root.TryGetProperty("promptFeedback", out var feedback) &&
            feedback.TryGetProperty("blockReason", out var block))
        {
            context = $" (prompt blocked: {block.GetString()})";
        }

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            throw new ImageGenerationException("no candidates in response" + context);
        }

        var candidate = candidates[0];
        var finish = candidate.TryGetProperty("finishReason", out var finishElement)
            ? finishElement.GetString()
            : null;

        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0)
        {
            throw new ImageGenerationException(
                $"no image in response (finishReason: {finish ?? "unknown"}){context}");
        }

        string? textPart = null;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inline))
            {
                var base64 = inline.GetProperty("data").GetString();
                if (string.IsNullOrEmpty(base64))
                {
                    continue;
                }

                var mime = inline.TryGetProperty("mimeType", out var mimeElement)
                    ? mimeElement.GetString() ?? "image/png"
                    : "image/png";
                return (Convert.FromBase64String(base64), mime);
            }

            if (part.TryGetProperty("text", out var textElement))
            {
                textPart = textElement.GetString();
            }
        }

        var note = textPart is null
            ? string.Empty
            : $" — model said: \"{Truncate(textPart)}\"";
        throw new ImageGenerationException(
            $"no image data (finishReason: {finish ?? "unknown"}){context}{note}");
    }

    private static string ErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return Truncate(message.GetString() ?? body);
            }
        }
        catch (JsonException)
        {
        }

        return Truncate(body);
    }

    private static string Truncate(string text)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }

    private sealed class TransientStatusException(string message) : Exception(message);
}

public static class ImageFile
{
    public static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png",
    };

    public static string SizeText(long bytes) =>
        (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
}
