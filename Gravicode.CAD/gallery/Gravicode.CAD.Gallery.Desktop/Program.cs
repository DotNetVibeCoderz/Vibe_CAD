using Avalonia;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;

namespace Gravicode.CAD.Gallery.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CadIO.RegisterAll();
        NativeEngine.Enable();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
