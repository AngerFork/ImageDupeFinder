namespace ImageDupeFinder.Core;

/// <summary>How an image in the folder being checked relates to the reference folder.</summary>
public enum MatchCategory
{
    /// <summary>Nothing comparable was found in the reference folder.</summary>
    New,

    /// <summary>A byte-for-byte identical file exists in the reference folder.</summary>
    Duplicate,

    /// <summary>Something in the reference folder looks the same, but the files are not identical.</summary>
    PossibleDuplicate,
}

/// <summary>Raw pixels and metadata produced by an <see cref="IImageDecoder"/>.</summary>
public sealed class DecodedImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>32-bit BGRA pixels, top-down rows, stride = Width * 4.</summary>
    public required byte[] Bgra { get; init; }

    /// <summary>Friendly format name, e.g. "JPEG" or "PNG".</summary>
    public string? Format { get; init; }

    /// <summary>EXIF orientation (1–8). 1 or null means "as stored".</summary>
    public int? Orientation { get; init; }

    public DateTime? DateTaken { get; init; }
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
}

/// <summary>Turns encoded image bytes into pixels. Platform-specific implementations live in the app.</summary>
public interface IImageDecoder
{
    /// <summary>Decodes the first frame of the image. Throws if the data can't be decoded.</summary>
    DecodedImage Decode(byte[] data, string path);
}

/// <summary>Everything the scanner learned about a single file.</summary>
public sealed class ImageRecord
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public string FileName => Path.GetFileName(FullPath);
    public required long FileSize { get; init; }
    public required DateTime LastModified { get; init; }

    /// <summary>SHA-256 of the file bytes (hex).</summary>
    public required string Sha256 { get; init; }

    public int Width { get; set; }
    public int Height { get; set; }
    public string? Format { get; set; }
    public DateTime? DateTaken { get; set; }
    public string? Camera { get; set; }
    public int? Orientation { get; set; }

    /// <summary>SHA-256 of the decoded (orientation-corrected) pixels. Equal values = visually identical pixels.</summary>
    public string? PixelHash { get; set; }

    /// <summary>64-bit DCT perceptual hash.</summary>
    public ulong? PHash { get; set; }

    /// <summary>64-bit gradient (difference) hash.</summary>
    public ulong? DHash { get; set; }

    /// <summary>Small PNG thumbnail (longest side <see cref="ScanOptions.ThumbnailSize"/>).</summary>
    public byte[]? ThumbnailPng { get; set; }

    /// <summary>Set when the file could not be decoded; only byte-level and name comparison apply then.</summary>
    public string? DecodeError { get; set; }

    public bool IsDecoded => PHash.HasValue;
    public string DimensionsText => Width > 0 ? $"{Width} × {Height}" : "—";
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}

/// <summary>One file in the reference folder that may be the same as the image being checked.</summary>
public sealed class MatchCandidate
{
    public required ImageRecord Reference { get; init; }

    /// <summary>True when the file bytes are identical.</summary>
    public bool IsExactFile { get; init; }

    /// <summary>True when decoded pixels are identical (even if the file bytes differ).</summary>
    public bool IsIdenticalPixels { get; init; }

    /// <summary>True when the only link is a shared file name.</summary>
    public bool IsNameOnly { get; init; }

    /// <summary>Visual similarity 0–100 based on perceptual hashes, or null when either image couldn't be decoded.</summary>
    public int? Similarity { get; init; }

    /// <summary>One-line explanation of why this is a match.</summary>
    public required string Reason { get; init; }

    /// <summary>Human-readable list of what differs between the two files.</summary>
    public required IReadOnlyList<string> Differences { get; init; }

    public int Rank => IsExactFile ? 0 : IsIdenticalPixels ? 1 : IsNameOnly ? 3 : 2;
}

/// <summary>Verdict for one image in the folder being checked.</summary>
public sealed class ScanResultItem
{
    public required ImageRecord Image { get; init; }
    public required MatchCategory Category { get; init; }

    /// <summary>Candidates in the reference folder, best first. Empty for <see cref="MatchCategory.New"/>.</summary>
    public required IReadOnlyList<MatchCandidate> Matches { get; init; }

    public MatchCandidate? BestMatch => Matches.Count > 0 ? Matches[0] : null;

    /// <summary>Short text for list views.</summary>
    public string Summary
    {
        get
        {
            var best = BestMatch;
            if (best is null)
                return Image.DecodeError is null ? "No match in folder 1" : "No match in folder 1 (could not read image; compared by file contents and name only)";
            string more = Matches.Count > 1 ? $" (+{Matches.Count - 1} more)" : "";
            return $"{best.Reason} → {best.Reference.RelativePath}{more}";
        }
    }
}

public sealed class ScanOptions
{
    /// <summary>Reference folder ("folder 1").</summary>
    public required string ReferenceFolder { get; init; }

    /// <summary>Folder being checked ("folder 2").</summary>
    public required string CheckFolder { get; init; }

    public bool IncludeSubfolders { get; init; } = true;

    /// <summary>
    /// How different two perceptual hashes may be (in bits, out of 64) and still count as a possible duplicate.
    /// 0 = only near-pixel-perfect; ~10 = resized/recompressed copies; 16+ = loose, more false positives.
    /// </summary>
    public int SimilarityTolerance { get; init; } = 10;

    /// <summary>Also treat "same file name, different picture" as a possible duplicate.</summary>
    public bool FlagSameFileName { get; init; } = true;

    /// <summary>Maximum match candidates kept per image.</summary>
    public int MaxCandidates { get; init; } = 10;

    public int ThumbnailSize { get; init; } = 96;

    /// <summary>Images decoded at once. Full-size decodes are memory hungry, so keep this modest.</summary>
    public int MaxParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount, 1, 4);

    public IReadOnlySet<string> Extensions { get; init; } = DefaultExtensions;

    public static readonly IReadOnlySet<string> DefaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff",
        ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp", ".ico",
    };
}

public sealed class ScanProgress
{
    public required string Stage { get; init; }
    public required int Done { get; init; }
    public required int Total { get; init; }
    public string? CurrentFile { get; init; }
}

public sealed class ScanResult
{
    public required ScanOptions Options { get; init; }
    public required IReadOnlyList<ImageRecord> ReferenceImages { get; init; }
    public required IReadOnlyList<ScanResultItem> Items { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required DateTime CompletedAt { get; init; }

    public IEnumerable<ScanResultItem> InCategory(MatchCategory category) => Items.Where(i => i.Category == category);
    public int Count(MatchCategory category) => Items.Count(i => i.Category == category);
}
