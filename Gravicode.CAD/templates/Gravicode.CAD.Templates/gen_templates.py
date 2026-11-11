"""Generates the dotnet-new template content folders (run from this directory).
Kept in the repo so templates can be regenerated consistently: python gen_templates.py
"""
import json
import os

V = "1.0.0"
CREDIT = "// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil\n"


def tj(short, name, desc, tags):
    return json.dumps({
        "$schema": "http://json.schemastore.org/template",
        "author": "Gravicode Studios (led by Kang Fadhil)",
        "classifications": ["CAD"] + tags,
        "identity": f"Gravicode.CAD.Templates.{short}",
        "name": name, "shortName": short, "description": desc,
        "sourceName": "CadApp", "preferNameDirectory": True,
        "tags": {"language": "C#", "type": "project"},
    }, indent=2) + "\n"


def w(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text.lstrip("\n"))


def proj(sdk, tfm, out, refs, extra=""):
    items = "\n".join(f'    <PackageReference Include="{n}" Version="{v}" />' for n, v in refs)
    output = f"\n    <OutputType>{out}</OutputType>" if out else ""
    return f"""
<Project Sdk="{sdk}">
  <PropertyGroup>{output}
    <TargetFramework>{tfm}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>{extra}
  </PropertyGroup>
  <ItemGroup>
{items}
  </ItemGroup>
</Project>
"""


# ---------------- cad-console ----------------
b = "content/cad-console/"
w(b + ".template.config/template.json", tj("cad-console", "Gravicode.CAD Console App",
  "Console app that builds a parametric part and exports STEP, STL, glTF, a PDF drawing and a PNG preview.", ["Console"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk", "net10.0", "Exe", [
    ("Gravicode.CAD.Parametric", V), ("Gravicode.CAD.IO", V), ("Gravicode.CAD.Rendering", V), ("Gravicode.CAD.Native", V)]))
w(b + "Program.cs", CREDIT + """using Gravicode.CAD;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

NativeEngine.Enable(); // Rust mesh kernels when available, C# otherwise

var model = Part.Create("Bracket")
    .Param("w", 120).Param("d", 80).Param("t", 10)
    .Box("w", "d", "t")
    .Holes(8.5, (-48, -28), (48, -28), (-48, 28), (48, 28))
    .Cylinder(36, 30).At(0, 0, "t")
    .Holes(20, (0, 0))
    .Build();

Directory.CreateDirectory("out");
model.Save("out/bracket.step");
model.ExportStl("out/bracket.stl");
model.Save("out/bracket.glb");
model.ExportPdf("out/bracket.pdf", "Bracket");
SoftwareRenderer.RenderToPng(model, "out/bracket.png");

var p = model.ToMesh().Properties;
Console.WriteLine($"{model.Name}: volume {p.Volume:N0} mm³, area {p.Area:N0} mm² → out/");
""")

# ---------------- cad-webapi ----------------
b = "content/cad-webapi/"
w(b + ".template.config/template.json", tj("cad-webapi", "Gravicode.CAD Web API",
  "ASP.NET Core minimal API: convert CAD files, generate parametric parts and text-to-CAD.", ["Web", "WebAPI"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk.Web", "net10.0", None, [("Gravicode.CAD.AI", V), ("Gravicode.CAD.Rendering", V)]))
w(b + "Program.cs", CREDIT + """using Gravicode.CAD;
using Gravicode.CAD.AI;
using Gravicode.CAD.IO;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

CadIO.RegisterAll();
var app = WebApplication.CreateBuilder(args).Build();

static string Mime(string ext) => ext switch
{
    "pdf" => "application/pdf", "png" => "image/png", "svg" => "image/svg+xml", "glb" => "model/gltf-binary",
    _ => "application/octet-stream"
};

app.MapGet("/", () => "Gravicode.CAD API: /formats, /parts/plate.step?w=150, POST /convert/stl, POST /ai/generate");

app.MapGet("/formats", () => new
{
    import = CadFormats.Importers.SelectMany(i => i.Extensions),
    export = CadFormats.Exporters.SelectMany(e => e.Extensions)
});

// GET /parts/plate.step?w=120&d=80&t=10&hole=8.5   (also .stl .glb .pdf .png ...)
app.MapGet("/parts/plate.{ext}", (string ext, double w = 120, double d = 80, double t = 10, double hole = 8.5) =>
{
    double x = w / 2 - 10, y = d / 2 - 10;
    var model = Part.Create("Plate").Box(w, d, t).Holes(hole, (-x, -y), (x, -y), (x, y), (-x, y)).Build();
    if (ext == "png") return Results.File(SoftwareRenderer.Render(model).EncodePng(), "image/png");
    return Results.File(model.SaveToBytes(ext), Mime(ext), $"plate.{ext}");
});

// POST /convert/stl   (multipart form field "file")
app.MapPost("/convert/{ext}", async (string ext, IFormFile file) =>
{
    await using var s = file.OpenReadStream();
    var model = CadModel.Load(s, Path.GetExtension(file.FileName));
    return Results.File(model.SaveToBytes(ext), Mime(ext), Path.ChangeExtension(file.FileName, ext));
}).DisableAntiforgery();

// POST /ai/generate   { "prompt": "Create a gearbox housing with four mounting holes" }
app.MapPost("/ai/generate", async (GenerateRequest req) =>
{
    var agent = new TextToCadAgent(ProviderFactory.FromEnvironment()); // ANTHROPIC_API_KEY → Claude, else offline
    var r = await agent.GenerateAsync(req.Prompt);
    return Results.Text(r.Definition.ToJson(), "application/json");
});

app.Run();

record GenerateRequest(string Prompt);
""")

# ---------------- cad-blazor ----------------
b = "content/cad-blazor/"
w(b + ".template.config/template.json", tj("cad-blazor", "Gravicode.CAD Blazor App",
  "Blazor Web App with the three.js CadViewer and live parametric sliders.", ["Web", "Blazor"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk.Web", "net10.0", None, [("Gravicode.CAD.ThreeJs", V), ("Gravicode.CAD.Parametric", V)]))
w(b + "Program.cs", """using CadApp.Components;
using Gravicode.CAD.IO;

CadIO.RegisterAll();
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
""")
w(b + "Components/App.razor", """<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <title>CadApp</title>
    <link rel="stylesheet" href="@Assets["CadApp.styles.css"]" />
    <ImportMap />
    <HeadOutlet @rendermode="InteractiveServer" />
    <style>
        body { margin: 0; font-family: system-ui, sans-serif; background: #0d1117; color: #e6edf3; }
        main { display: grid; grid-template-columns: 300px 1fr; height: 100vh; }
        aside { padding: 20px; border-right: 1px solid #222a35; }
        label { display: block; margin: 14px 0 4px; font-size: 13px; color: #8b97a8; }
        input[type=range] { width: 100%; }
    </style>
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
""")
w(b + "Components/Routes.razor", """<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData"><RouteView RouteData="routeData" /></Found>
</Router>
""")
w(b + "Components/_Imports.razor", """@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Gravicode.CAD
@using Gravicode.CAD.Parametric
@using Gravicode.CAD.ThreeJs
""")
w(b + "Components/Pages/Home.razor", """@page "/"

<main>
    <aside>
        <h2>Flange</h2>
        <label>Outer diameter: @_od mm</label>
        <input type="range" min="60" max="220" @bind="_od" @bind:event="oninput" @bind:after="Rebuild" />
        <label>Bolt holes: @_bolts</label>
        <input type="range" min="3" max="16" @bind="_bolts" @bind:event="oninput" @bind:after="Rebuild" />
        <label>Thickness: @_t mm</label>
        <input type="range" min="6" max="30" @bind="_t" @bind:event="oninput" @bind:after="Rebuild" />
        <p style="font-size:12px;color:#8b97a8;margin-top:24px">Made with Gravicode.CAD</p>
    </aside>
    <CadViewer Model="_model" Height="100vh" />
</main>

@code {
    private double _od = 140, _t = 14;
    private int _bolts = 8;
    private CadModel? _model;

    protected override void OnInitialized() => Rebuild();

    private void Rebuild()
    {
        var def = new PartDefinition { Name = "Flange" };
        def.Parameters["od"] = _od; def.Parameters["t"] = _t; def.Parameters["id"] = "od / 3";
        def.Features.Add(new CylinderFeature { Diameter = "od", Height = "t", Color = "#C8734A" });
        def.Features.Add(new HoleFeature { Diameter = "id", Z = "t" });
        def.Features.Add(new PatternFeature { Kind = "circular", Count = _bolts, Source = new HoleFeature { Diameter = 10, X = "od * 0.38", Z = "t" } });
        _model = def.Build();
    }
}
""")

# ---------------- cad-wpf ----------------
b = "content/cad-wpf/"
w(b + ".template.config/template.json", tj("cad-wpf", "Gravicode.CAD WPF App",
  "WPF desktop app with a CadViewport control (orbit/pan/zoom) and file open.", ["Desktop", "WPF"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk", "net10.0-windows", "WinExe",
  [("Gravicode.CAD.Viewer.Wpf", V), ("Gravicode.CAD.Parametric", V)], "\n    <UseWPF>true</UseWPF>"))
w(b + "App.xaml", """<Application x:Class="CadApp.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml" />
""")
w(b + "App.xaml.cs", "namespace CadApp;\n\npublic partial class App : System.Windows.Application { }\n")
w(b + "MainWindow.xaml", """<Window x:Class="CadApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:gcad="clr-namespace:Gravicode.CAD.Viewer.Wpf;assembly=Gravicode.CAD.Viewer.Wpf"
        Title="CadApp" Width="1100" Height="720">
    <DockPanel>
        <ToolBar DockPanel.Dock="Top">
            <Button Content="Open…" Click="OnOpen" />
            <Button Content="Fit" Click="OnFit" />
        </ToolBar>
        <gcad:CadViewport x:Name="Viewport" />
    </DockPanel>
</Window>
""")
w(b + "MainWindow.xaml.cs", CREDIT + """using System.Windows;
using Gravicode.CAD.Parametric;
using Microsoft.Win32;

namespace CadApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Viewport.Model = CadModel.Load("engine.step") works the same way
        Viewport.Model = Part.Create("Demo").Box(80, 60, 12).Holes(10, (0, 0)).Gear(2, 24, 10, 10).At(0, 0, 12).Build();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CAD files|*.step;*.stp;*.stl;*.obj;*.glb;*.gltf;*.3mf;*.dxf;*.igs;*.iges" };
        if (dlg.ShowDialog(this) == true) Viewport.Source = dlg.FileName;
    }

    private void OnFit(object sender, RoutedEventArgs e) => Viewport.View.Fit();
}
""")

# ---------------- cad-winforms ----------------
b = "content/cad-winforms/"
w(b + ".template.config/template.json", tj("cad-winforms", "Gravicode.CAD WinForms App",
  "WinForms desktop app with viewport.Load(\"engine.step\").", ["Desktop", "WinForms"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk", "net10.0-windows", "WinExe",
  [("Gravicode.CAD.Viewer.WinForms", V), ("Gravicode.CAD.Parametric", V)], "\n    <UseWindowsForms>true</UseWindowsForms>"))
w(b + "Program.cs", CREDIT + """using Gravicode.CAD.Parametric;
using Gravicode.CAD.Viewer.WinForms;

ApplicationConfiguration.Initialize();
var form = new Form { Text = "CadApp", Width = 1100, Height = 720 };
var viewport = new CadViewport { Dock = DockStyle.Fill };
var open = new Button { Text = "Open…", Dock = DockStyle.Top, Height = 32 };
open.Click += (_, _) =>
{
    using var dlg = new OpenFileDialog { Filter = "CAD files|*.step;*.stp;*.stl;*.obj;*.glb;*.3mf;*.dxf;*.igs" };
    if (dlg.ShowDialog(form) == DialogResult.OK) viewport.Load(dlg.FileName);   // e.g. viewport.Load("engine.step")
};
form.Controls.Add(viewport);
form.Controls.Add(open);
viewport.Model = Part.Create("Demo").Tube(30, 22, 40).Build();
Application.Run(form);
""")

# ---------------- cad-avalonia ----------------
b = "content/cad-avalonia/"
w(b + ".template.config/template.json", tj("cad-avalonia", "Gravicode.CAD Avalonia App",
  "Cross-platform desktop app (Avalonia 12) rendering CAD models through ThreeNet (DirectX 12 / Metal / Vulkan).", ["Desktop", "Avalonia"]))
w(b + "CadApp.csproj", proj("Microsoft.NET.Sdk", "net10.0", "WinExe", [
    ("Avalonia.Desktop", "12.1.2"), ("Avalonia.Themes.Fluent", "12.1.2"), ("ThreeNet.Avalonia", "0.12.0"),
    ("Gravicode.CAD.ThreeNet", V), ("Gravicode.CAD.Parametric", V)]))
w(b + "Program.cs", CREDIT + """using Avalonia;
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
""")

# ---------------- cad-maui ----------------
b = "content/cad-maui/"
w(b + ".template.config/template.json", tj("cad-maui", "Gravicode.CAD MAUI App",
  ".NET MAUI app showing a parametric CAD part preview with a slider.", ["Mobile", "MAUI"]))
w(b + "CadApp.csproj", f"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-android</TargetFrameworks>
    <TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <UseMaui>true</UseMaui>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <SingleProject>true</SingleProject>
    <ApplicationTitle>CadApp</ApplicationTitle>
    <ApplicationId>com.companyname.cadapp</ApplicationId>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">24.0</SupportedOSPlatformVersion>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.19041.0</SupportedOSPlatformVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
    <PackageReference Include="Gravicode.CAD.Parametric" Version="{V}" />
    <PackageReference Include="Gravicode.CAD.Rendering" Version="{V}" />
  </ItemGroup>
</Project>
""")
w(b + "MauiProgram.cs", """namespace CadApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}
""")
w(b + "App.cs", CREDIT + """using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

namespace CadApp;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}

public class MainPage : ContentPage
{
    private readonly Image _image = new() { Aspect = Aspect.AspectFit };
    private readonly Slider _teeth = new(8, 60, 24);
    private readonly Label _label = new() { HorizontalOptions = LayoutOptions.Center, TextColor = Colors.White };

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#0D1117");
        _teeth.ValueChanged += async (_, _) => await RenderAsync();
        var grid = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        grid.Add(_image, 0, 0); grid.Add(_label, 0, 1); grid.Add(_teeth, 0, 2);
        Content = grid;
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        int teeth = (int)_teeth.Value;
        _label.Text = $"Spur gear · {teeth} teeth";
        var png = await Task.Run(() =>
        {
            var model = Part.Create("Gear").Gear(2, teeth, 10, 8).Build();
            return SoftwareRenderer.Render(model, new RenderOptions { Width = 900, Height = 700 }).EncodePng();
        });
        _image.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }
}
""")
w(b + "Platforms/Android/MainActivity.cs", """using Android.App;
using Android.Content.PM;

namespace CadApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public class MainActivity : MauiAppCompatActivity { }
""")
w(b + "Platforms/Android/MainApplication.cs", """using Android.App;
using Android.Runtime;

namespace CadApp;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
""")
w(b + "Platforms/Android/AndroidManifest.xml", """<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <application android:allowBackup="true" android:supportsRtl="true"></application>
</manifest>
""")
w(b + "Platforms/Windows/App.xaml", """<maui:MauiWinUIApplication x:Class="CadApp.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:maui="using:Microsoft.Maui" />
""")
w(b + "Platforms/Windows/App.xaml.cs", """namespace CadApp.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
""")
w(b + "Platforms/Windows/Package.appxmanifest", """<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
  <Identity Name="CadApp" Publisher="CN=User Name" Version="1.0.0.0" />
  <Properties><DisplayName>CadApp</DisplayName><PublisherDisplayName>User Name</PublisherDisplayName><Logo>$placeholder$.png</Logo></Properties>
  <Dependencies><TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0" /><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.19041.0" /></Dependencies>
  <Resources><Resource Language="x-generate" /></Resources>
  <Applications><Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$"><uap:VisualElements DisplayName="CadApp" Description="CadApp" Square150x150Logo="$placeholder$.png" Square44x44Logo="$placeholder$.png" BackgroundColor="transparent" /></Application></Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
""")
print("templates written")
