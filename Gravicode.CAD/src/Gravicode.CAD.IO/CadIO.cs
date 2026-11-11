using System.Globalization;
using System.Runtime.CompilerServices;

namespace Gravicode.CAD.IO;

/// <summary>Registers all built-in formats with <see cref="CadFormats"/>.</summary>
public static class CadIO
{
    private static int _done;

    /// <summary>Idempotent. Runs automatically when this assembly loads.</summary>
    public static void RegisterAll()
    {
        if (Interlocked.Exchange(ref _done, 1) == 1) return;
        foreach (var f in new object[] { new StlFormat(), new ObjFormat(), new GltfFormat(), new ThreeMfFormat(), new DxfFormat(), new SvgFormat(), new StepFormat(), new IgesFormat(), new PdfExporter() })
        {
            if (f is ICadImporter i) CadFormats.Register(i);
            if (f is ICadExporter e) CadFormats.Register(e);
        }
        CadFormats.RegisterUnsupported("dwg",
            "DWG is a proprietary binary format. Convert it to DXF first (e.g. ODA File Converter, LibreCAD or AutoCAD 'SAVEAS DXF'), then load the .dxf. Native DWG reading is on the roadmap (see PLAN.md).");
    }

    [ModuleInitializer]
    internal static void Init() => RegisterAll();

    internal static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    internal static string F(double v) => v.ToString("0.########", Inv);
    internal static string G(double v) => v.ToString("R", Inv);
    internal static double D(string s) => double.Parse(s.Trim(), NumberStyles.Float, Inv);
    internal static bool TryD(string s, out double v) => double.TryParse(s.Trim(), NumberStyles.Float, Inv, out v);
}

/// <summary>Convenience import/export calls matching the documented API.</summary>
public static class CadModelIOExtensions
{
    public static void ExportStl(this CadModel m, string path, bool binary = true) => m.Save(path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) ? path : path + ".stl", new ExportOptions { Binary = binary });
    public static void ExportStep(this CadModel m, string path) => m.Save(path);
    public static void ExportObj(this CadModel m, string path) => m.Save(path);
    public static void ExportGltf(this CadModel m, string path) => m.Save(path);
    public static void ExportDxf(this CadModel m, string path) => m.Save(path);
    public static void ExportSvg(this CadModel m, string path) => m.Save(path);
    public static void ExportPdf(this CadModel m, string path, string? title = null) => m.Save(path, new ExportOptions { Title = title ?? m.Name });
    public static void Export3mf(this CadModel m, string path) => m.Save(path);
    public static void ExportIges(this CadModel m, string path) => m.Save(path);
}

public static class StepImporter { public static CadModel Import(string path) { CadIO.RegisterAll(); return CadModel.Load(path); } }
public static class StlImporter { public static CadModel Import(string path) { CadIO.RegisterAll(); return CadModel.Load(path); } }
public static class DxfImporter { public static CadModel Import(string path) { CadIO.RegisterAll(); return CadModel.Load(path); } }
public static class GltfImporter { public static CadModel Import(string path) { CadIO.RegisterAll(); return CadModel.Load(path); } }
