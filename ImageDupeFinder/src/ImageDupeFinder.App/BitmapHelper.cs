using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App;

internal static class BitmapHelper
{
    public static Bitmap FromBgra(byte[] px, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }

    /// <summary>Decodes PNG bytes into a Bitmap that doesn't depend on the source stream staying open.</summary>
    public static Bitmap? FromPng(byte[]? png)
    {
        if (png is null) return null;
        using var ms = new MemoryStream(png, writable: false);
        using var img = Image.FromStream(ms);
        return new Bitmap(img);
    }

    /// <summary>Upright, downscaled preview of a file, or a placeholder explaining why there isn't one.</summary>
    public static Bitmap LoadPreview(string path, IImageDecoder decoder, int maxSize)
    {
        try
        {
            var d = decoder.Decode(File.ReadAllBytes(path), path);
            var (px, w, h) = PixelOps.ApplyOrientation(d.Bgra, d.Width, d.Height, d.Orientation);
            var (fx, fw, fh) = PixelOps.Fit(px, w, h, maxSize);
            return FromBgra(fx, fw, fh);
        }
        catch (Exception ex)
        {
            return Placeholder(ex is FileNotFoundException or DirectoryNotFoundException
                ? "File no longer exists"
                : "Preview not available\n" + ex.Message);
        }
    }

    /// <summary>Pads an image into a size×size square (for list thumbnails).</summary>
    public static Bitmap Square(Image? src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        if (src is null)
        {
            using var pen = new Pen(Color.Silver, 1) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, 2, 2, size - 5, size - 5);
            using var f = new Font(FontFamily.GenericSansSerif, size / 3f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "?", f, new Rectangle(0, 0, size, size), Color.Gray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return bmp;
        }
        double s = Math.Min((double)(size - 2) / src.Width, (double)(size - 2) / src.Height);
        s = Math.Min(s, 1.0);
        int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
        g.DrawImage(src, new Rectangle((size - w) / 2, (size - h) / 2, w, h));
        return bmp;
    }

    public static Bitmap Placeholder(string text, int w = 480, int h = 320)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(245, 245, 245));
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var f = new Font(FontFamily.GenericSansSerif, 16f, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, text, f, new Rectangle(16, 16, w - 32, h - 32), Color.DimGray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.WordBreak);
        return bmp;
    }
}
