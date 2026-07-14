using System.Buffers.Binary;

namespace CheckMind.App.Core;

public static class ImageGeometry
{
    public static (int Width, int Height) GetSize(byte[] imageBytes)
    {
        if (TryGetPngSize(imageBytes, out var pngW, out var pngH))
        {
            return (pngW, pngH);
        }

        if (TryGetJpegSize(imageBytes, out var jpgW, out var jpgH))
        {
            return (jpgW, jpgH);
        }

        throw new InvalidOperationException("Unsupported image format for size parsing.");
    }

    public static BBox FromRelative(double x, double y, double width, double height, int imageWidth, int imageHeight)
    {
        var absX = (int)Math.Round(x * imageWidth);
        var absY = (int)Math.Round(y * imageHeight);
        var absW = (int)Math.Round(width * imageWidth);
        var absH = (int)Math.Round(height * imageHeight);

        absX = Math.Clamp(absX, 0, Math.Max(0, imageWidth - 1));
        absY = Math.Clamp(absY, 0, Math.Max(0, imageHeight - 1));
        absW = Math.Clamp(absW, 1, Math.Max(1, imageWidth - absX));
        absH = Math.Clamp(absH, 1, Math.Max(1, imageHeight - absY));

        return new BBox(absX, absY, absW, absH);
    }

    private static bool TryGetPngSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length < 24)
        {
            return false;
        }

        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!bytes.AsSpan(0, 8).SequenceEqual(sig))
        {
            return false;
        }

        var ihdrType = bytes.AsSpan(12, 4);
        ReadOnlySpan<byte> expectedType = stackalloc byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        if (!ihdrType.SequenceEqual(expectedType))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryGetJpegSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return false;
        }

        var i = 2;
        while (i + 3 < bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }

            while (i < bytes.Length && bytes[i] == 0xFF)
            {
                i++;
            }

            if (i >= bytes.Length)
            {
                break;
            }

            var marker = bytes[i++];
            if (marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            if (i + 1 >= bytes.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i, 2));
            if (segmentLength < 2 || i + segmentLength > bytes.Length)
            {
                break;
            }

            if (IsSofMarker(marker))
            {
                if (segmentLength < 7)
                {
                    break;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 3, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 5, 2));
                return width > 0 && height > 0;
            }

            i += segmentLength;
        }

        return false;
    }

    private static bool IsSofMarker(byte marker)
    {
        return marker is 0xC0 or 0xC1 or 0xC2 or 0xC3
            or 0xC5 or 0xC6 or 0xC7
            or 0xC9 or 0xCA or 0xCB
            or 0xCD or 0xCE or 0xCF;
    }
}
