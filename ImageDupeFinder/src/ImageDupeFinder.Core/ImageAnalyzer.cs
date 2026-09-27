using System.Security.Cryptography;

namespace ImageDupeFinder.Core;

/// <summary>Reads one file and produces its <see cref="ImageRecord"/> (hashes, metadata, thumbnail).</summary>
public sealed class ImageAnalyzer(IImageDecoder decoder, int thumbnailSize = 96)
{
    public ImageRecord Analyze(string fullPath, string rootFolder)
    {
        var info = new FileInfo(fullPath);
        byte[] data = File.ReadAllBytes(fullPath);

        var record = new ImageRecord
        {
            FullPath = info.FullName,
            RelativePath = Path.GetRelativePath(rootFolder, info.FullName),
            FileSize = info.Length,
            LastModified = info.LastWriteTime,
            Sha256 = Convert.ToHexString(SHA256.HashData(data)),
        };

        DecodedImage decoded;
        try
        {
            decoded = decoder.Decode(data, fullPath);
        }
        catch (Exception ex)
        {
            record.DecodeError = ex.Message;
            record.Format = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();
            return record;
        }

        var (px, w, h) = PixelOps.ApplyOrientation(decoded.Bgra, decoded.Width, decoded.Height, decoded.Orientation);

        record.Width = w;
        record.Height = h;
        record.Format = decoded.Format;
        record.Orientation = decoded.Orientation;
        record.DateTaken = decoded.DateTaken;
        record.Camera = JoinCamera(decoded.CameraMake, decoded.CameraModel);
        record.PixelHash = HashPixels(px, w, h);
        record.PHash = PerceptualHash.ComputePHash(px, w, h);
        record.DHash = PerceptualHash.ComputeDHash(px, w, h);

        var (tb, tw, th) = PixelOps.Fit(px, w, h, thumbnailSize);
        record.ThumbnailPng = PngEncoder.Encode(tb, tw, th);
        return record;
    }

    private static string HashPixels(byte[] px, int w, int h)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> dims = stackalloc byte[8];
        BitConverter.TryWriteBytes(dims, w);
        BitConverter.TryWriteBytes(dims[4..], h);
        sha.AppendData(dims);
        sha.AppendData(px);
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static string? JoinCamera(string? make, string? model)
    {
        make = make?.Trim().TrimEnd('\0');
        model = model?.Trim().TrimEnd('\0');
        if (string.IsNullOrEmpty(make)) return string.IsNullOrEmpty(model) ? null : model;
        if (string.IsNullOrEmpty(model)) return make;
        // Many cameras repeat the make in the model ("Canon" + "Canon EOS R6").
        return model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? model : $"{make} {model}";
    }
}
