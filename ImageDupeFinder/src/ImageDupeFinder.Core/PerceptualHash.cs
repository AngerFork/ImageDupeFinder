using System.Numerics;

namespace ImageDupeFinder.Core;

/// <summary>
/// Perceptual hashes: images that look alike produce hashes that differ in only a few bits,
/// even after resizing, recompression, or small colour/brightness changes.
/// </summary>
public static class PerceptualHash
{
    private const int DctSize = 32;
    private static readonly double[,] Cos = BuildCosTable();

    private static double[,] BuildCosTable()
    {
        var t = new double[DctSize, DctSize];
        for (int u = 0; u < DctSize; u++)
            for (int x = 0; x < DctSize; x++)
                t[u, x] = Math.Cos((2 * x + 1) * u * Math.PI / (2 * DctSize));
        return t;
    }

    /// <summary>DCT-based hash (pHash): robust to scaling, compression and mild edits.</summary>
    public static ulong ComputePHash(byte[] bgra, int w, int h)
    {
        var g = PixelOps.Grayscale(bgra, w, h, DctSize, DctSize);

        // Separable 2D DCT-II; we only need the low-frequency 9×9 corner.
        const int keep = 9;
        var rows = new double[DctSize, keep];
        for (int y = 0; y < DctSize; y++)
            for (int u = 0; u < keep; u++)
            {
                double s = 0;
                for (int x = 0; x < DctSize; x++) s += g[y * DctSize + x] * Cos[u, x];
                rows[y, u] = s;
            }

        // Take coefficients (1..8, 1..8), skipping the DC row/column which only carry overall brightness.
        var coeffs = new double[64];
        for (int v = 1; v < keep; v++)
            for (int u = 1; u < keep; u++)
            {
                double s = 0;
                for (int y = 0; y < DctSize; y++) s += rows[y, u] * Cos[v, y];
                coeffs[(v - 1) * 8 + (u - 1)] = s;
            }

        var sorted = (double[])coeffs.Clone();
        Array.Sort(sorted);
        double median = (sorted[31] + sorted[32]) / 2;

        ulong hash = 0;
        for (int i = 0; i < 64; i++)
            if (coeffs[i] > median) hash |= 1UL << i;
        return hash;
    }

    /// <summary>Gradient hash (dHash): compares each pixel with its right-hand neighbour on a 9×8 grid.</summary>
    public static ulong ComputeDHash(byte[] bgra, int w, int h)
    {
        var g = PixelOps.Grayscale(bgra, w, h, 9, 8);
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++, bit++)
                if (g[y * 9 + x] > g[y * 9 + x + 1]) hash |= 1UL << bit;
        return hash;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>0–100 similarity from a 64-bit Hamming distance.</summary>
    public static int ToSimilarity(int distance) => (int)Math.Round((64 - distance) * 100.0 / 64);
}
