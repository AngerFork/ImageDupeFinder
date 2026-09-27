namespace ImageDupeFinder.Core;

/// <summary>One property shown side by side for the checked image and a reference candidate.</summary>
public sealed record ComparisonRow(string Property, string CheckValue, string ReferenceValue, bool Differs);

public static class Comparison
{
    /// <summary>Property-by-property comparison, for the side-by-side table.</summary>
    public static IReadOnlyList<ComparisonRow> Compare(ImageRecord check, ImageRecord reference)
    {
        var rows = new List<ComparisonRow>();
        void Add(string name, string a, string b, bool? differs = null) =>
            rows.Add(new ComparisonRow(name, a, b, differs ?? !string.Equals(a, b, StringComparison.Ordinal)));

        Add("File name", check.FileName, reference.FileName,
            !string.Equals(check.FileName, reference.FileName, StringComparison.OrdinalIgnoreCase));
        Add("Location", Path.GetDirectoryName(check.FullPath) ?? "", Path.GetDirectoryName(reference.FullPath) ?? "", false);
        Add("Dimensions", check.DimensionsText, reference.DimensionsText);
        Add("File size", FormatSize(check.FileSize), FormatSize(reference.FileSize), check.FileSize != reference.FileSize);
        Add("Format", check.Format ?? "—", reference.Format ?? "—");
        Add("Date taken", FormatDate(check.DateTaken), FormatDate(reference.DateTaken));
        Add("Camera", check.Camera ?? "—", reference.Camera ?? "—");
        Add("Orientation tag", check.Orientation?.ToString() ?? "—", reference.Orientation?.ToString() ?? "—");
        Add("Last modified", check.LastModified.ToString("yyyy-MM-dd HH:mm:ss"), reference.LastModified.ToString("yyyy-MM-dd HH:mm:ss"));
        Add("File contents", ShortHash(check.Sha256), ShortHash(reference.Sha256));
        Add("Pixel data", ShortHash(check.PixelHash), ShortHash(reference.PixelHash),
            check.PixelHash is null || reference.PixelHash is null ? false : check.PixelHash != reference.PixelHash);
        return rows;
    }

    /// <summary>Plain-language list of what differs, omitting noise (location, raw hashes).</summary>
    public static IReadOnlyList<string> Differences(ImageRecord check, ImageRecord reference)
    {
        var skip = new HashSet<string> { "Location", "File contents", "Pixel data" };
        return Compare(check, reference)
            .Where(r => r.Differs && !skip.Contains(r.Property))
            .Select(r => $"{r.Property}: {r.CheckValue} vs {r.ReferenceValue}")
            .ToList();
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.##} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    private static string FormatDate(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
    private static string ShortHash(string? h) => h is null ? "—" : h[..12].ToLowerInvariant() + "…";
}
