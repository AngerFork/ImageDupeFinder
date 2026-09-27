namespace ImageDupeFinder.Core;

/// <summary>Small, dependency-free pixel helpers operating on 32-bit BGRA buffers.</summary>
public static class PixelOps
{
    /// <summary>Applies an EXIF orientation (1–8) so the pixels are upright as a viewer would show them.</summary>
    public static (byte[] Bgra, int Width, int Height) ApplyOrientation(byte[] src, int w, int h, int? orientation)
    {
        int o = orientation ?? 1;
        if (o < 2 || o > 8) return (src, w, h);

        bool swap = o >= 5;
        int ow = swap ? h : w, oh = swap ? w : h;
        var dst = new byte[ow * oh * 4];

        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < ow; x++)
            {
                (int sx, int sy) = o switch
                {
                    2 => (w - 1 - x, y),              // mirror horizontal
                    3 => (w - 1 - x, h - 1 - y),      // rotate 180
                    4 => (x, h - 1 - y),              // mirror vertical
                    5 => (y, x),                      // transpose
                    6 => (y, h - 1 - x),              // rotate 90 CW
                    7 => (w - 1 - y, h - 1 - x),      // transverse
                    _ => (w - 1 - y, x),              // 8: rotate 90 CCW
                };
                Buffer.BlockCopy(src, (sy * w + sx) * 4, dst, (y * ow + x) * 4, 4);
            }
        }
        return (dst, ow, oh);
    }

    /// <summary>Box-filter (area average) downscale of a BGRA buffer. Never upscales.</summary>
    public static byte[] Resize(byte[] src, int w, int h, int tw, int th)
    {
        var dst = new byte[tw * th * 4];
        for (int ty = 0; ty < th; ty++)
        {
            int y0 = (int)((long)ty * h / th), y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * h / th));
            for (int tx = 0; tx < tw; tx++)
            {
                int x0 = (int)((long)tx * w / tw), x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * w / tw));
                long b = 0, g = 0, r = 0, a = 0;
                for (int y = y0; y < y1; y++)
                {
                    int i = (y * w + x0) * 4;
                    for (int x = x0; x < x1; x++, i += 4)
                    {
                        b += src[i]; g += src[i + 1]; r += src[i + 2]; a += src[i + 3];
                    }
                }
                long n = (long)(y1 - y0) * (x1 - x0);
                int d = (ty * tw + tx) * 4;
                dst[d] = (byte)(b / n); dst[d + 1] = (byte)(g / n); dst[d + 2] = (byte)(r / n); dst[d + 3] = (byte)(a / n);
            }
        }
        return dst;
    }

    /// <summary>Fits (w, h) inside a max×max box, keeping aspect ratio. Never upscales.</summary>
    public static (int Width, int Height) FitWithin(int w, int h, int max)
    {
        if (w <= max && h <= max) return (w, h);
        double s = Math.Min((double)max / w, (double)max / h);
        return (Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)));
    }

    /// <summary>Downscales to fit within max×max (for previews and thumbnails).</summary>
    public static (byte[] Bgra, int Width, int Height) Fit(byte[] src, int w, int h, int max)
    {
        var (tw, th) = FitWithin(w, h, max);
        return tw == w && th == h ? (src, w, h) : (Resize(src, w, h, tw, th), tw, th);
    }

    /// <summary>Area-averaged grayscale image of exactly tw×th (aspect ratio is deliberately ignored, as hashing expects).</summary>
    public static double[] Grayscale(byte[] src, int w, int h, int tw, int th)
    {
        var small = Resize(src, w, h, tw, th);
        var gray = new double[tw * th];
        for (int i = 0; i < gray.Length; i++)
        {
            int p = i * 4;
            // Composite transparent pixels over white so transparency doesn't read as black.
            double a = small[p + 3] / 255.0;
            double b = small[p] * a + 255 * (1 - a);
            double g = small[p + 1] * a + 255 * (1 - a);
            double r = small[p + 2] * a + 255 * (1 - a);
            gray[i] = 0.299 * r + 0.587 * g + 0.114 * b;
        }
        return gray;
    }
}
