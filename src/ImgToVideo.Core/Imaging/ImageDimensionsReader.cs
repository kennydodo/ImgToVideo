using System.Buffers.Binary;

namespace ImgToVideo.Core.Imaging;

public static class ImageDimensionsReader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryRead(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> firstBytes = stackalloc byte[2];
            if (!TryReadExactly(fs, firstBytes))
            {
                return false;
            }

            if (firstBytes[0] == 0xFF && firstBytes[1] == 0xD8)
            {
                return TryReadJpeg(fs, out width, out height);
            }

            Span<byte> signature = stackalloc byte[8];
            firstBytes.CopyTo(signature);
            if (!TryReadExactly(fs, signature[2..]))
            {
                return false;
            }

            if (signature.SequenceEqual(PngSignature))
            {
                return TryReadPng(fs, out width, out height);
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReadPng(FileStream fs, out int width, out int height)
    {
        width = 0;
        height = 0;

        Span<byte> header = stackalloc byte[16];
        if (!TryReadExactly(fs, header))
        {
            return false;
        }

        if (header[4] != (byte)'I' || header[5] != (byte)'H' || header[6] != (byte)'D' || header[7] != (byte)'R')
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(header[8..12]);
        height = BinaryPrimitives.ReadInt32BigEndian(header[12..16]);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(FileStream fs, out int width, out int height)
    {
        width = 0;
        height = 0;

        Span<byte> lengthBytes = stackalloc byte[2];
        while (true)
        {
            int b;
            do
            {
                b = fs.ReadByte();
                if (b < 0)
                {
                    return false;
                }
            } while (b != 0xFF);

            int marker;
            do
            {
                marker = fs.ReadByte();
                if (marker < 0)
                {
                    return false;
                }
            } while (marker == 0xFF);

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                continue;
            }

            if (!TryReadExactly(fs, lengthBytes))
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2)
            {
                return false;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isStartOfFrame)
            {
                Span<byte> sof = stackalloc byte[5];
                if (!TryReadExactly(fs, sof))
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(sof[1..3]);
                width = BinaryPrimitives.ReadUInt16BigEndian(sof[3..5]);
                return width > 0 && height > 0;
            }

            fs.Seek(length - 2, SeekOrigin.Current);
        }
    }

    private static bool TryReadExactly(FileStream fs, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = fs.Read(buffer[read..]);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }
}
