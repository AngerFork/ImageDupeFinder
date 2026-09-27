using System;
using System.IO;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App.Decoding;

/// <summary>Tries GDI+ first (common formats), then Windows Imaging Component (WebP, HEIC, AVIF...).</summary>
internal sealed class WindowsImageDecoder : IImageDecoder
{
    public DecodedImage Decode(byte[] data, string path)
    {
        try
        {
            return GdiImageDecoder.Decode(data);
        }
        catch (Exception)
        {
            // GDI+ reports unsupported formats as ArgumentException or (famously) OutOfMemoryException.
            try
            {
                return WicImageDecoder.Decode(data);
            }
            catch (Exception)
            {
                throw new InvalidDataException(
                    "Windows couldn't decode this file (format not supported, codec not installed, or file damaged).");
            }
        }
    }
}
