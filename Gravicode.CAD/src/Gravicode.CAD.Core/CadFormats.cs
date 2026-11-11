using System.Reflection;

namespace Gravicode.CAD;

public record ImportOptions
{
    public string? SourcePath { get; init; }
    /// <summary>Chordal tolerance used when curved geometry has to be tessellated.</summary>
    public double ChordTolerance { get; init; } = 0.05;
    /// <summary>Unit assumed when the file does not declare one (STL/OBJ).</summary>
    public LengthUnit DefaultUnit { get; init; } = LengthUnit.Millimeter;
}

public record ExportOptions
{
    /// <summary>Binary vs ASCII when the format supports both (STL, glTF=GLB).</summary>
    public bool Binary { get; init; } = true;
    public double ChordTolerance { get; init; } = 0.05;
    public string Author { get; init; } = "Gravicode.CAD";
    public string Organization { get; init; } = "Gravicode Studios";
    /// <summary>Drawing title (PDF/SVG title block).</summary>
    public string? Title { get; init; }
    /// <summary>Lower-case target extension (set by <see cref="CadModel.Save(string, ExportOptions?)"/>), e.g. "gltf" vs "glb".</summary>
    public string? TargetExtension { get; init; }
}

public interface ICadImporter
{
    string FormatName { get; }
    IReadOnlyList<string> Extensions { get; }
    CadModel Import(Stream stream, ImportOptions options);
}

public interface ICadExporter
{
    string FormatName { get; }
    IReadOnlyList<string> Extensions { get; }
    void Export(CadModel model, Stream stream, ExportOptions options);
}

/// <summary>Registry of importers and exporters keyed by file extension.</summary>
public static class CadFormats
{
    private static readonly Dictionary<string, ICadImporter> _importers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ICadExporter> _exporters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _unsupported = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _gate = new();
    private static bool _bootstrapped;

    public static void Register(ICadImporter importer)
    {
        lock (_gate) foreach (var e in importer.Extensions) _importers[Norm(e)] = importer;
    }

    public static void Register(ICadExporter exporter)
    {
        lock (_gate) foreach (var e in exporter.Extensions) _exporters[Norm(e)] = exporter;
    }

    /// <summary>Marks an extension as recognised but unsupported, with guidance for the user.</summary>
    public static void RegisterUnsupported(string extension, string guidance)
    {
        lock (_gate) _unsupported[Norm(extension)] = guidance;
    }

    public static ICadImporter? FindImporter(string pathOrExt)
    {
        EnsureBootstrapped();
        lock (_gate) return _importers.GetValueOrDefault(Norm(pathOrExt));
    }

    public static ICadExporter? FindExporter(string pathOrExt)
    {
        EnsureBootstrapped();
        lock (_gate) return _exporters.GetValueOrDefault(Norm(pathOrExt));
    }

    public static IReadOnlyList<ICadImporter> Importers { get { EnsureBootstrapped(); lock (_gate) return [.. _importers.Values.Distinct()]; } }
    public static IReadOnlyList<ICadExporter> Exporters { get { EnsureBootstrapped(); lock (_gate) return [.. _exporters.Values.Distinct()]; } }

    public static string UnsupportedMessage(string pathOrExt, bool import)
    {
        EnsureBootstrapped();
        var ext = Norm(pathOrExt);
        lock (_gate)
            if (_unsupported.TryGetValue(ext, out var g)) return g;
        return $"No {(import ? "importer" : "exporter")} registered for '.{ext}'. Reference Gravicode.CAD.IO (or call CadIO.RegisterAll()).";
    }

    private static string Norm(string pathOrExt)
    {
        var e = Path.GetExtension(pathOrExt);
        if (string.IsNullOrEmpty(e)) e = pathOrExt;
        return e.TrimStart('.').ToLowerInvariant();
    }

    /// <summary>
    /// Lets <c>CadModel.Load</c> work with only Gravicode.CAD.Core referenced directly:
    /// if Gravicode.CAD.IO is deployed next to the app it is loaded and registers itself.
    /// </summary>
    private static void EnsureBootstrapped()
    {
        if (_bootstrapped) return;
        _bootstrapped = true;
        if (_importers.Count > 0) return;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Gravicode.CAD.IO")
                      ?? Assembly.Load("Gravicode.CAD.IO");
            asm.GetType("Gravicode.CAD.IO.CadIO")?.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }
        catch { /* IO package not present: only explicitly registered formats are available */ }
    }
}
