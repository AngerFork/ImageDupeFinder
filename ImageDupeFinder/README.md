# Image Duplicate Finder (.NET 8, WinForms)

Compares a **folder to check (folder 2)** against a **reference folder (folder 1)** and sorts every image in folder 2 into:

| Category | Meaning |
|---|---|
| **New** | Nothing comparable in folder 1. |
| **Duplicate** | A byte-for-byte identical file exists in folder 1 (any name, any subfolder). |
| **Possible Duplicate** | Looks the same as something in folder 1, but the files differ: stripped/edited metadata, re-saved, resized, re-compressed, lightly edited, or the same file name with a different picture. |

Selecting an image shows it **side by side with its folder-1 match**, with a property table (dimensions, size, format, date taken, camera, orientation, modified date, content/pixel hashes) where differing rows are highlighted. If there are several candidates, pick between them from the drop-down. **Open** / **Show in folder** buttons (and a right-click menu) let you check each file by hand.

## Build and run

Requires Windows and the .NET 8 SDK (Visual Studio 2022 17.8+ has it). No NuGet packages are used.

- Visual Studio: open `ImageDupeFinder.sln` and press F5 (`ImageDupeFinder.App` is the startup project).
- Command line: `dotnet run --project src\ImageDupeFinder.App -c Release`
- Single exe: `dotnet publish src\ImageDupeFinder.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`

## Using it

1. Pick (or drag from Explorer) folder 1 and folder 2, then **Scan**.
2. Review the **Possible Duplicate** tab first (it opens automatically when there are any), then **Duplicate**.
3. Double-click a list row or a preview to open that image in your default viewer. Click a column header to sort.
4. **Export HTML report…** saves a self-contained report with thumbnails and clickable file links.

**Similarity tolerance** (0–24, default 10) sets how different two pictures may look and still count as a possible duplicate. Changing it after a scan re-sorts the results instantly without rescanning.
- 0–4: near-identical only
- 8–10: resized or re-compressed copies
- 12–16: also light crops and edits, with more false alarms

Folders, options and tolerance are remembered in `%AppData%\ImageDupeFinder\settings.json`.

## How matching works

For every image the scanner computes:

1. **SHA-256 of the file bytes.** An equal hash means an identical file → **Duplicate**.
2. **SHA-256 of the decoded pixels** (after applying EXIF rotation). Equal pixels with different file bytes means only metadata or encoding changed → **Possible Duplicate**.
3. **Perceptual hashes**: a 64-bit DCT hash (pHash) plus a gradient hash (dHash). A small bit distance means the pictures look alike even after resizing or re-compression → **Possible Duplicate**. Both hashes have to agree, and wildly different aspect ratios are rejected, to cut down on false alarms.
4. **Same file name** with a different picture → **Possible Duplicate** (you can switch this off).

## Formats

JPEG, PNG, GIF, BMP, TIFF and ICO are decoded with GDI+. WebP, HEIC/HEIF, AVIF and JPEG XR go through the Windows Imaging Component. HEIC and AVIF need the free *HEIF Image Extensions* / *AV1 Video Extension* from the Microsoft Store. A file that can't be decoded is still compared by exact contents and name, and it's flagged in the results.

## Project layout

```
src/ImageDupeFinder.Core   net8.0 class library: hashing, matching, HTML report (platform-neutral, no dependencies)
src/ImageDupeFinder.App    net8.0-windows WinForms UI + Windows image decoders (GDI+, WIC)
```

The core takes an `IImageDecoder`, so the same engine could run behind a console app or ASP.NET Core front end.
