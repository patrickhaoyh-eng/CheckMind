using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace CheckMind.App.Core;

public static class ImageCropper
{
    public static byte[]? TryCropToPngBytes(byte[] imageBytes, BBox roi)
    {
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return null;
        }

        try
        {
            using var input = new MemoryStream(imageBytes, writable: false);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            var x = Math.Clamp(roi.X, 0, frame.PixelWidth - 1);
            var y = Math.Clamp(roi.Y, 0, frame.PixelHeight - 1);
            var w = Math.Clamp(roi.Width, 1, frame.PixelWidth - x);
            var h = Math.Clamp(roi.Height, 1, frame.PixelHeight - y);

            if (x == 0 && y == 0 && w == frame.PixelWidth && h == frame.PixelHeight)
            {
                return null;
            }

            var cropped = new CroppedBitmap(frame, new Int32Rect(x, y, w, h));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cropped));

            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
