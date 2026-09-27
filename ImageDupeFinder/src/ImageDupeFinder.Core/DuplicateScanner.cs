using System.Collections.Concurrent;
using System.Diagnostics;

namespace ImageDupeFinder.Core;

/// <summary>Compares every image in the check folder against the reference folder.</summary>
public sealed class DuplicateScanner(IImageDecoder decoder)
{
    public async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string refRoot = Path.GetFullPath(options.ReferenceFolder);
        string checkRoot = Path.GetFullPath(options.CheckFolder);
        if (!Directory.Exists(refRoot)) throw new DirectoryNotFoundException($"Folder 1 not found: {refRoot}");
        if (!Directory.Exists(checkRoot)) throw new DirectoryNotFoundException($"Folder 2 not found: {checkRoot}");

        progress?.Report(new ScanProgress { Stage = "Finding images", Done = 0, Total = 0 });
        var checkFiles = await Task.Run(() => FindImages(checkRoot, options), ct);
        var checkSet = new HashSet<string>(checkFiles, StringComparer.OrdinalIgnoreCase);
        // If folder 2 sits inside folder 1 (with subfolders on), don't compare files with themselves.
        var refFiles = (await Task.Run(() => FindImages(refRoot, options), ct)).Where(f => !checkSet.Contains(f)).ToList();

        var analyzer = new ImageAnalyzer(decoder, options.ThumbnailSize);
        int total = refFiles.Count + checkFiles.Count, done = 0;

        var all = refFiles.Select(f => (Path: f, Root: refRoot, IsRef: true))
            .Concat(checkFiles.Select(f => (Path: f, Root: checkRoot, IsRef: false)))
            .ToList();
        var records = new ConcurrentDictionary<string, ImageRecord>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(all,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
            (f, token) =>
            {
                try
                {
                    records[f.Path] = analyzer.Analyze(f.Path, f.Root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Unreadable file (locked, permissions, vanished). Skip it rather than failing the scan.
                }
                int n = Interlocked.Increment(ref done);
                progress?.Report(new ScanProgress { Stage = "Analysing images", Done = n, Total = total, CurrentFile = f.Path });
                return ValueTask.CompletedTask;
            });

        progress?.Report(new ScanProgress { Stage = "Comparing", Done = total, Total = total });

        var refs = refFiles.Where(records.ContainsKey).Select(f => records[f]).ToList();
        var checks = checkFiles.Where(records.ContainsKey).Select(f => records[f]).ToList();

        var items = await Task.Run(() => Classify(checks, refs, options, ct), ct);

        return new ScanResult
        {
            Options = options,
            ReferenceImages = refs,
            Items = items,
            Elapsed = sw.Elapsed,
            CompletedAt = DateTime.Now,
        };
    }

    /// <summary>Pure matching step, separated so it can be tested without touching the disk.</summary>
    public static IReadOnlyList<ScanResultItem> Classify(
        IReadOnlyList<ImageRecord> checks, IReadOnlyList<ImageRecord> refs, ScanOptions options, CancellationToken ct = default)
    {
        var bySha = refs.ToLookup(r => r.Sha256);
        var byPixels = refs.Where(r => r.PixelHash is not null).ToLookup(r => r.PixelHash!);
        var byName = refs.ToLookup(r => r.FileName, StringComparer.OrdinalIgnoreCase);
        var decodedRefs = refs.Where(r => r.IsDecoded).ToList();
        int tol = Math.Clamp(options.SimilarityTolerance, 0, 32);

        var results = new ScanResultItem[checks.Count];
        Parallel.For(0, checks.Count, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var img = checks[i];
            var found = new Dictionary<string, MatchCandidate>(StringComparer.OrdinalIgnoreCase);

            void Add(ImageRecord r, bool exact, bool pixels, bool nameOnly, int? sim, string reason)
            {
                if (found.ContainsKey(r.FullPath)) return;
                found[r.FullPath] = new MatchCandidate
                {
                    Reference = r,
                    IsExactFile = exact,
                    IsIdenticalPixels = pixels,
                    IsNameOnly = nameOnly,
                    Similarity = sim,
                    Reason = reason,
                    Differences = Comparison.Differences(img, r),
                };
            }

            int? SimilarityTo(ImageRecord r) =>
                img.PHash is ulong a && r.PHash is ulong b ? PerceptualHash.ToSimilarity(PerceptualHash.Distance(a, b)) : null;

            // 1. Byte-for-byte identical files.
            foreach (var r in bySha[img.Sha256])
                Add(r, true, img.IsDecoded, false, img.IsDecoded ? 100 : null, "Identical file");

            // 2. Same pixels, different file (metadata stripped/edited, lossless re-save, different container).
            if (img.PixelHash is not null)
                foreach (var r in byPixels[img.PixelHash])
                    Add(r, false, true, false, 100, "Identical pixels, file differs (metadata or encoding)");

            // 3. Looks the same (resized, recompressed, lightly edited).
            if (img.PHash is ulong ph && img.DHash is ulong dh)
            {
                foreach (var r in decodedRefs)
                {
                    int dp = PerceptualHash.Distance(ph, r.PHash!.Value);
                    if (dp > tol) continue;
                    // The gradient hash must broadly agree too; this weeds out coincidental pHash collisions
                    // (e.g. two different mostly-flat images).
                    int dd = PerceptualHash.Distance(dh, r.DHash!.Value);
                    if (dd > tol + 8) continue;
                    // Very different shapes (portrait vs landscape) aren't the same picture unless cropped heavily.
                    if (r.AspectRatio > 0 && img.AspectRatio > 0 &&
                        Math.Abs(Math.Log(r.AspectRatio / img.AspectRatio)) > 0.35) continue;

                    int sim = PerceptualHash.ToSimilarity(dp);
                    Add(r, false, false, false, sim, sim >= 98
                        ? $"Looks the same ({sim}% similar), but files differ"
                        : $"Looks {sim}% similar");
                }
            }

            // 4. Same name, but nothing above matched.
            if (options.FlagSameFileName)
                foreach (var r in byName[img.FileName])
                    Add(r, false, false, true, SimilarityTo(r), "Same file name, different image");

            var matches = found.Values
                .OrderBy(m => m.Rank)
                .ThenByDescending(m => m.Similarity ?? -1)
                .ThenBy(m => m.Reference.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, options.MaxCandidates))
                .ToList();

            var category = matches.Count == 0 ? MatchCategory.New
                : matches[0].IsExactFile ? MatchCategory.Duplicate
                : MatchCategory.PossibleDuplicate;

            results[i] = new ScanResultItem { Image = img, Category = category, Matches = matches };
        });

        return results.OrderBy(r => r.Image.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> FindImages(string root, ScanOptions options)
    {
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden,
        };
        return Directory.EnumerateFiles(root, "*", enumOptions)
            .Where(f => options.Extensions.Contains(Path.GetExtension(f)))
            .Select(Path.GetFullPath)
            .ToList();
    }
}
