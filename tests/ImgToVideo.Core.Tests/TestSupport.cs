namespace ImgToVideo.Core.Tests;

public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        var ihdr = new byte[13];
        WriteBigEndian32(ihdr, 0, (uint)width);
        WriteBigEndian32(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // color type: truecolor RGB
        // bytes 10-12: compression, filter, interlace = 0

        var stride = width * 3;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (stride + 1);
            raw[rowStart] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                raw[rowStart + 1 + x * 3] = (byte)(x * 255 / Math.Max(1, width - 1));
                raw[rowStart + 2 + x * 3] = (byte)(y * 255 / Math.Max(1, height - 1));
                raw[rowStart + 3 + x * 3] = 128;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest))
        {
            zlib.Write(raw);
        }

        var png = new List<byte>(1 << 16);
        png.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        AppendChunk(png, "IHDR", ihdr);
        AppendChunk(png, "IDAT", compressed.ToArray());
        AppendChunk(png, "IEND", []);
        return [.. png];
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

    private static void AppendChunk(List<byte> png, string type, byte[] data)
    {
        var length = (uint)data.Length;
        png.AddRange([
            (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length,
        ]);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        png.AddRange(typeBytes);
        png.AddRange(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, typeBytes.Length);
        var crc = Crc32(crcInput);
        png.AddRange([
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc,
        ]);
    }

    private static void WriteBigEndian32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = (crc & 1) == 1 ? 0xEDB88320u : 0u;
                crc = (crc >> 1) ^ mask;
            }
        }

        return ~crc;
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
