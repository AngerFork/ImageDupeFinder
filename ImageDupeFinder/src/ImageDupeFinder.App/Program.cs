using System;
using System.Windows.Forms;

namespace ImageDupeFinder.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
#if !TYPECHECK
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
