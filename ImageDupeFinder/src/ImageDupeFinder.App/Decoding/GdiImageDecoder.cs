using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App.Decoding;

/// <summary>GDI+ (System.Drawing) decoder: JPEG, PNG, GIF, BMP, TIFF, ICO. Fast and reliable for the common formats.</summary>
internal static class GdiImageDecoder
{
    private const int TagOrientation = 0x0112;
    private const int TagMake = 0x010F;
    private const int TagModel = 0x0110;
    private const int TagDateTimeOriginal = 0x9003;
    private const int TagDateTimeDigitized = 0x9004;

    public static DecodedImage Decode(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var img = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);
        if (img is not Bitmap bmp) throw new InvalidDataException("Not a raster image.");

        int w = bmp.Width, h = bmp.Height;
        var px = new byte[w * h * 4];
        // Asking LockBits for 32bpp ARGB makes GDI+ convert from whatever the source format is.
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(bd.Scan0 + y * bd.Stride, px, y * w * 4, w * 4);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }

        return new DecodedImage
        {
            Width = w,
            Height = h,
            Bgra = px,
            Format = FormatName(img.RawFormat),
            Orientation = GetUShort(img, TagOrientation),
            DateTaken = ParseExifDate(GetAscii(img, TagDateTimeOriginal) ?? GetAscii(img, TagDateTimeDigitized)),
            CameraMake = GetAscii(img, TagMake),
            CameraModel = GetAscii(img, TagModel),
        };
    }

    private static string FormatName(ImageFormat f)
    {
        Guid g = f.Guid;
        if (g == ImageFormat.Jpeg.Guid) return "JPEG";
        if (g == ImageFormat.Png.Guid) return "PNG";
        if (g == ImageFormat.Gif.Guid) return "GIF";
        if (g == ImageFormat.Bmp.Guid || g == ImageFormat.MemoryBmp.Guid) return "BMP";
        if (g == ImageFormat.Tiff.Guid) return "TIFF";
        if (g == ImageFormat.Icon.Guid) return "ICO";
        return "Image";
    }

    private static bool Has(Image img, int id) => img.PropertyIdList.Contains(id);

    private static string? GetAscii(Image img, int id)
    {
        if (!Has(img, id)) return null;
        try
        {
            var v = img.GetPropertyItem(id)?.Value;
            if (v is null) return null;
            var s = Encoding.ASCII.GetString(v).TrimEnd('\0').Trim();
            return s.Length == 0 ? null : s;
        }
        catch (ArgumentException) { return null; }
    }

    private static int? GetUShort(Image img, int id)
    {
        if (!Has(img, id)) return null;
        try
        {
            var v = img.GetPropertyItem(id)?.Value;
            return v is { Length: >= 2 } ? BitConverter.ToUInt16(v, 0) : null;
        }
        catch (ArgumentException) { return null; }
    }

    internal static DateTime? ParseExifDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formats = { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy:MM:dd" };
        return DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
