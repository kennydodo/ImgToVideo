namespace ImgToVideo.Core.Tests;

public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        var bytes = new List<byte>
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        };
        bytes.AddRange(BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(width))
            : BitConverter.GetBytes(width));
        bytes.AddRange(BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(height))
            : BitConverter.GetBytes(height));
        bytes.AddRange([0x08, 0x06, 0x00, 0x00, 0x00]);
        return [.. bytes];
    }

    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte>
        {
            0xFF, 0xD8,
            0xFF, 0xC0,
            0x00, 0x0B,
            0x08,
        };
        bytes.Add((byte)(height >> 8));
        bytes.Add((byte)(height & 0xFF));
        bytes.Add((byte)(width >> 8));
        bytes.Add((byte)(width & 0xFF));
        bytes.AddRange([0x01, 0x01, 0x00, 0x01, 0x01, 0x00]);
        return [.. bytes];
    }
}

public sealed class TempProject : IDisposable
{
    public string Path { get; }

    public TempProject()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "imgtovideo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "images"));
    }

    public string WriteImage(string fileName, int width, int height)
    {
        var fullPath = System.IO.Path.Combine(Path, "images", fileName);
        File.WriteAllBytes(fullPath, TestImages.Png(width, height));
        return fullPath;
    }

    public string WriteSilenceWav(string fileName, double seconds, int sampleRate = 44100)
    {
        var fullPath = System.IO.Path.Combine(Path, fileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        var samples = (int)(seconds * sampleRate);
        var dataSize = samples * 2;

        using var writer = new BinaryWriter(File.Create(fullPath));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        for (var i = 0; i < samples; i++)
        {
            writer.Write((short)0);
        }

        writer.Flush();
        return fullPath;
    }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void WriteAudio() =>
        WriteFile(
            System.IO.Path.Combine("audio", "narration.mp3"),
            "fake audio bytes - not probed by unit tests");

    public void WriteSrt() =>
        WriteFile(
            "narration.srt",
            """
            1
            00:00:00,000 --> 00:00:05,000
            First line one.

            2
            00:00:05,200 --> 00:00:09,500
            First line two.

            3
            00:00:10,200 --> 00:00:15,000
            Second line one.

            4
            00:00:15,200 --> 00:00:19,000
            Second line two.
            """);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class FFmpegProbe
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            var result = new ImgToVideo.Ffmpeg.FfmpegRunner("ffmpeg")
                .RunAsync(["-version"])
                .GetAwaiter()
                .GetResult();
            return result.Success;
        }
        catch (Exception)
        {
            return false;
        }
    });

    public static bool IsAvailable => Available.Value;
}
