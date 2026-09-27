using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App.Decoding;

/// <summary>
/// Windows Imaging Component decoder (via WPF). Used for formats GDI+ can't read: WebP, HEIC/HEIF, AVIF, JPEG XR.
/// HEIC/AVIF need the free "HEIF Image Extensions" / "AV1 Video Extension" from the Microsoft Store.
/// </summary>
internal static class WicImageDecoder
{
    public static DecodedImage Decode(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        var decoder = BitmapDecoder.Create(ms,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        BitmapFrame frame = decoder.Frames[0];
        BitmapSource src = frame;
        if (src.Format != PixelFormats.Bgra32)
            src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        int w = src.PixelWidth, h = src.PixelHeight;
        var px = new byte[w * h * 4];
        src.CopyPixels(px, w * 4, 0);

        string? format = null, make = null, model = null;
        DateTime? taken = null;
        int? orientation = null;

        try
        {
            format = decoder.CodecInfo?.FriendlyName?.Replace(" Decoder", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        catch (NotSupportedException) { }

        try
        {
            if (frame.Metadata is BitmapMetadata md)
            {
                make = md.CameraManufacturer;
                model = md.CameraModel;
                if (DateTime.TryParse(md.DateTaken, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d)) taken = d;
                if (md.GetQuery("System.Photo.Orientation") is ushort o) orientation = o;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Some codecs expose no (or unreadable) metadata; pixels are what matter.
        }

        return new DecodedImage
        {
            Width = w,
            Height = h,
            Bgra = px,
            Format = string.IsNullOrEmpty(format) ? null : format,
            Orientation = orientation,
            DateTaken = taken,
            CameraMake = make,
            CameraModel = model,
        };
    }
}
