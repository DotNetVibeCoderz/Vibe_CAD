using Avalonia;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;

namespace Gravicode.CAD.Studio.Desktop;

// Gravicode CAD Studio (desktop) — Made by Gravicode Studios, led by Kang Fadhil.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CadIO.RegisterAll();
        NativeEngine.Enable();
        Software3D = args.Contains("--software-3d") || Environment.GetEnvironmentVariable("GCAD_SOFTWARE_3D") == "1";
        StartupFile = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>A file passed on the command line (e.g. double-clicking a .gcproj).</summary>
    public static string? StartupFile { get; private set; }

    /// <summary>Use the CPU renderer for the 3D pane (no GPU / remote desktop).</summary>
    public static bool Software3D { get; private set; }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
