// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using System.Windows;
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
