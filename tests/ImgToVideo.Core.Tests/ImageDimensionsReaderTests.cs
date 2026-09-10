using ImgToVideo.Core.Imaging;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ImageDimensionsReaderTests : IDisposable
{
    private readonly string _path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"imgtovideo-img-{Guid.NewGuid():N}.bin");

    [Fact]
    public void Reads_png_dimensions()
    {
        File.WriteAllBytes(_path, TestImages.Png(2304, 1296));

        Assert.True(ImageDimensionsReader.TryRead(_path, out var width, out var height));
        Assert.Equal(2304, width);
        Assert.Equal(1296, height);
    }

    [Fact]
    public void Reads_jpeg_dimensions()
    {
        File.WriteAllBytes(_path, TestImages.Jpeg(1920, 1080));

        Assert.True(ImageDimensionsReader.TryRead(_path, out var width, out var height));
        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
    }

    [Fact]
    public void Rejects_garbage()
    {
        File.WriteAllBytes(_path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        Assert.False(ImageDimensionsReader.TryRead(_path, out _, out _));
    }

    [Fact]
    public void Rejects_png_without_ihdr()
    {
        var bytes = new List<byte>
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            (byte)'J', (byte)'U', (byte)'N', (byte)'K',
        };
        bytes.AddRange(new byte[16]);
        File.WriteAllBytes(_path, [.. bytes]);

        Assert.False(ImageDimensionsReader.TryRead(_path, out _, out _));
    }

    [Fact]
    public void Rejects_missing_file()
    {
        Assert.False(ImageDimensionsReader.TryRead(_path, out _, out _));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
