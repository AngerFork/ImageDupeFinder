using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace ImageDupeFinder.App;

/// <summary>Opening files and folders with Windows Explorer / the default viewer.</summary>
internal static class Shell
{
    public static void OpenFile(string path)
    {
        if (!Exists(path)) return;
        Run(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void ShowInFolder(string path)
    {
        if (!Exists(path)) return;
        Run(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private static bool Exists(string path)
    {
        if (File.Exists(path)) return true;
        MessageBox.Show($"This file no longer exists:\n{path}", "Image Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private static void Run(ProcessStartInfo psi)
    {
        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Image Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
