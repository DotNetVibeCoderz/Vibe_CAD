// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.ThreeNet;
using ThreeNet.Avalonia;

AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);

class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var scene = new CadScene();
        scene.SetModel(Part.Create("Gear").Gear(2.5, 28, 12, 14).Build());
        var view = new ThreeNetView { Scene = scene.Scene, Camera = scene.Camera };
        var orbit = new OrbitController(view, scene.Camera);
        var (center, radius) = scene.Frame();
        orbit.Target = center; orbit.Distance = radius * 3.2f;
        view.Frame += (_, _) => orbit.Apply();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.MainWindow = new Window { Title = "CadApp", Width = 1100, Height = 720, Content = view };
        base.OnFrameworkInitializationCompleted();
    }
}
