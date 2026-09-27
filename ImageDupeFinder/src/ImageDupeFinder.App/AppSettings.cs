using System;
using System.IO;
using System.Text.Json;

namespace ImageDupeFinder.App;

/// <summary>Remembers the last folders and options in %AppData%\ImageDupeFinder\settings.json.</summary>
internal sealed class AppSettings
{
    public string Folder1 { get; set; } = "";
    public string Folder2 { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public int SimilarityTolerance { get; set; } = 10;
    public bool FlagSameFileName { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ImageDupeFinder", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
