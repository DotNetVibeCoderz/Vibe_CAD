using Gravicode.CAD.Studio.Catalog;
using Gravicode.CAD.Studio.Commands;

namespace Gravicode.CAD.Studio.Web;

/// <summary>Per-circuit editing session: the open document, the command engine and view state.</summary>
public sealed class StudioSession
{
    public StudioDocument Document { get; private set; }
    public CommandEngine Engine { get; }
    public string ViewMode { get; set; } = "2d";          // 2d | 3d | split
    public bool Indonesian { get; set; }
    public string? FileName { get; set; }
    public event Action? Changed;
    public event Action<string>? ViewRequested;

    public StudioSession()
    {
        Document = StudioCatalog.Blank();
        Engine = new CommandEngine(Document);
        Engine.StateChanged += () => Changed?.Invoke();
        Engine.ViewRequest += v =>
        {
            if (v == "view-3d") ViewMode = ViewMode == "2d" ? "split" : ViewMode;
            if (v == "view-2d") ViewMode = "2d";
            ViewRequested?.Invoke(v);
            Changed?.Invoke();
        };
        Hook(Document);
    }

    private void Hook(StudioDocument d) => d.Changed += () => Changed?.Invoke();

    public void Load(StudioDocument doc, string? fileName = null)
    {
        Document = doc;
        FileName = fileName;
        Hook(doc);
        Engine.SetDocument(doc);
        Engine.Print($"Opened \"{doc.Name}\" — {doc.Entities.Count} objects, {doc.Solids.Count} solids. Type HELP for commands.");
        ViewMode = doc.Solids.Count > 0 ? "split" : "2d";
        Changed?.Invoke();
        ViewRequested?.Invoke("zoom-extents");
    }

    public event Action<SnapResult>? CursorMoved;
    public void RaiseCursor(SnapResult s) => CursorMoved?.Invoke(s);

    public string T(string en, string id) => Indonesian ? id : en;
    public void Notify() => Changed?.Invoke();
}
