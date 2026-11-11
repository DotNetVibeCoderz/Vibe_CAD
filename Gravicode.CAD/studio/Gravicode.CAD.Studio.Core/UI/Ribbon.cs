namespace Gravicode.CAD.Studio.UI;

/// <summary>A ribbon button: runs <see cref="Command"/> (a command name, or an app action prefixed with "app:").</summary>
public sealed record RibbonButton(string Label, string Command, string Icon, string Tooltip, bool Large = false);
public sealed record RibbonGroup(string Title, IReadOnlyList<RibbonButton> Buttons);
public sealed record RibbonTab(string Title, string TitleId, IReadOnlyList<RibbonGroup> Groups);

/// <summary>Ribbon layout shared by the web and desktop editions.</summary>
public static class StudioRibbon
{
    public static IReadOnlyList<RibbonTab> Tabs { get; } =
    [
        new("Home", "Beranda",
        [
            new("Draw", [B("Line", "LINE", "line", "Line (L)", true), B("Polyline", "PLINE", "pline", "Polyline (PL)", true), B("Circle", "CIRCLE", "circle", "Circle (C)"), B("Arc", "ARC", "arc", "3-point arc (A)"),
                         B("Rectangle", "RECTANG", "rect", "Rectangle (REC)"), B("Polygon", "POLYGON", "polygon", "Polygon (POL)"), B("Spline", "SPLINE", "spline", "Spline (SPL)"), B("Point", "POINT", "point", "Point (PO)")]),
            new("Modify", [B("Move", "MOVE", "move", "Move (M)"), B("Copy", "COPY", "copy", "Copy (CO)"), B("Rotate", "ROTATE", "rotate", "Rotate (RO)"), B("Scale", "SCALE", "scale", "Scale (SC)"),
                           B("Mirror", "MIRROR", "mirror", "Mirror (MI)"), B("Offset", "OFFSET", "offset", "Offset (O)"), B("Array", "ARRAY", "array", "Array (AR)"), B("Explode", "EXPLODE", "explode", "Explode (X)"), B("Erase", "ERASE", "erase", "Erase (E)")]),
            new("Edit", [B("Trim", "TRIM", "trim", "Trim to crossing objects (TR)"), B("Extend", "EXTEND", "extend", "Extend to the next object (EX)"),
                         B("Fillet", "FILLET", "fillet", "Round a corner (F)"), B("Chamfer", "CHAMFER", "chamfer", "Bevel a corner (CHA)")]),
            new("Annotate", [B("Text", "TEXT", "text", "Text (T)"), B("Dimension", "DIMLINEAR", "dim", "Aligned dimension (DLI)"), B("Hatch", "HATCH", "hatch", "Hatch or fill an area (H)")]),
            new("Blocks", [B("Block", "BLOCK", "block", "Make a block from objects (B)"), B("Insert", "INSERT", "insert", "Insert a block (I)")]),
            new("Layers", [B("Layers", "LAYER", "layers", "Layer manager (LA)", true), B("Colour", "COLOR", "color", "Current colour (COL)")]),
        ]),
        new("Solid", "Solid",
        [
            new("Primitives", [B("Box", "BOX", "box", "Box", true), B("Cylinder", "CYLINDER", "cylinder", "Cylinder (CYL)", true), B("Cone", "CONE", "cone", "Cone"), B("Sphere", "SPHERE", "sphere", "Sphere"), B("Torus", "TORUS", "torus", "Torus (TOR)"), B("Gear", "GEAR", "gear", "Involute spur gear")]),
            new("From sketch", [B("Extrude", "EXTRUDE", "extrude", "Extrude closed shapes (EXT)", true), B("Revolve", "REVOLVE", "revolve", "Revolve profile (REV)", true)]),
            new("Booleans", [B("Union", "UNION", "union", "Union (UNI)"), B("Subtract", "SUBTRACT", "subtract", "Subtract (SU)"), B("Intersect", "INTERSECT", "intersect", "Intersect (IN)"), B("Elevate", "ELEVATE", "elevate", "Move solids in Z (EL)")]),
        ]),
        new("Inspect", "Periksa",
        [
            new("Measure", [B("Distance", "DIST", "dist", "Distance (DI)", true), B("Area", "AREA", "area", "Area (AA)"), B("Mass props", "MASSPROP", "mass", "Mass properties (MP)"), B("List", "LIST", "list", "List objects (LI)")]),
            new("History", [B("Undo", "UNDO", "undo", "Undo (Ctrl+Z)"), B("Redo", "REDO", "redo", "Redo (Ctrl+Y)")]),
        ]),
        new("View", "Tampilan",
        [
            new("Viewport", [B("Plan", "app:view-2d", "plan", "2D drafting view", true), B("Split", "app:view-split", "split", "2D + 3D side by side", true), B("3D", "app:view-3d", "cube3d", "3D model view", true), B("Extents", "ZOOM", "zoom", "Zoom extents (Z)")]),
            new("Aids", [B("Ortho", "ORTHO", "ortho", "Ortho mode (F8)"), B("Snap", "GRID", "grid", "Grid snap (F9)"), B("OSnap", "OSNAP", "osnap", "Object snap (F3)")]),
        ]),
        new("AI", "AI",
        [
            new("Text to CAD", [B("Generate part", "AI", "ai", "Describe a part and insert it (AI)", true), B("Help", "HELP", "help", "List all commands")]),
        ]),
        new("Output", "Keluaran",
        [
            new("File", [B("New", "app:new", "new", "Start screen", true), B("Open", "app:open", "open", "Open .gcproj or any CAD file", true), B("Save", "app:save", "save", "Save project (.gcproj)", true)]),
            new("Plot", [B("Plot", "PLOT", "plot", "Plot to a PDF/SVG sheet with scale and title block (PLOT)", true)]),
            new("Export", [B("STEP", "app:export:step", "export", "STEP AP214"), B("STL", "app:export:stl", "export", "Binary STL"), B("glTF", "app:export:glb", "export", "glTF binary"), B("3MF", "app:export:3mf", "export", "3MF"),
                           B("DXF", "app:export:dxf", "export", "AutoCAD DXF"), B("PDF", "app:export:pdf", "export", "PDF drawing"), B("SVG", "app:export:svg", "export", "SVG"), B("PNG", "app:export:png", "export", "Rendered image")]),
        ]),
    ];

    private static RibbonButton B(string label, string cmd, string icon, string tip, bool large = false) => new(label, cmd, icon, tip, large);

    /// <summary>24×24 stroke icons (SVG path data) drawn for the studio.</summary>
    public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>
    {
        ["line"] = "M4 20 L20 4 M4 20 m-1.5 0 a1.5 1.5 0 1 0 3 0 a1.5 1.5 0 1 0 -3 0 M20 4 m-1.5 0 a1.5 1.5 0 1 0 3 0 a1.5 1.5 0 1 0 -3 0",
        ["pline"] = "M3 18 L8 7 L14 15 L21 5",
        ["circle"] = "M12 4 a8 8 0 1 0 0.01 0 Z M12 12 L18 9",
        ["arc"] = "M4 18 A10 10 0 0 1 20 18",
        ["rect"] = "M4 6 H20 V18 H4 Z",
        ["polygon"] = "M12 3 L20 8 L20 16 L12 21 L4 16 L4 8 Z",
        ["spline"] = "M3 17 C7 3 12 21 21 6",
        ["point"] = "M12 9 V15 M9 12 H15 M12 12 m-5 0 a5 5 0 1 0 10 0 a5 5 0 1 0 -10 0",
        ["move"] = "M12 3 V21 M3 12 H21 M12 3 L9 6 M12 3 L15 6 M12 21 L9 18 M12 21 L15 18 M3 12 L6 9 M3 12 L6 15 M21 12 L18 9 M21 12 L18 15",
        ["copy"] = "M8 8 H19 V19 H8 Z M5 16 V5 H16",
        ["rotate"] = "M19 12 A7 7 0 1 1 15 5.7 M15 2 V6 H19",
        ["scale"] = "M4 20 V13 M4 20 H11 M4 20 L20 4 M20 4 H14 M20 4 V10",
        ["mirror"] = "M12 3 V21 M9 6 L4 18 H9 Z M15 6 L20 18 H15 Z",
        ["offset"] = "M4 18 L10 6 H20 M4 22 L12 8 H22",
        ["array"] = "M4 4 H8 V8 H4 Z M10 4 H14 V8 H10 Z M16 4 H20 V8 H16 Z M4 10 H8 V14 H4 Z M10 10 H14 V14 H10 Z M16 10 H20 V14 H16 Z M4 16 H8 V20 H4 Z M10 16 H14 V20 H10 Z M16 16 H20 V20 H16 Z",
        ["explode"] = "M12 2 V7 M12 17 V22 M2 12 H7 M17 12 H22 M5 5 L8 8 M16 16 L19 19 M19 5 L16 8 M8 16 L5 19",
        ["erase"] = "M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 21 H17 L18 7",
        ["text"] = "M5 5 H19 M12 5 V20 M9 20 H15",
        ["dim"] = "M4 6 V18 M20 6 V18 M4 12 H20 M4 12 L7 10 M4 12 L7 14 M20 12 L17 10 M20 12 L17 14",
        ["layers"] = "M12 3 L21 8 L12 13 L3 8 Z M3 12 L12 17 L21 12 M3 16 L12 21 L21 16",
        ["color"] = "M12 3 C7 9 6 12 6 15 A6 6 0 0 0 18 15 C18 12 17 9 12 3 Z",
        ["box"] = "M4 8 L12 4 L20 8 V16 L12 20 L4 16 Z M4 8 L12 12 L20 8 M12 12 V20",
        ["cylinder"] = "M5 6 A7 3 0 0 0 19 6 A7 3 0 0 0 5 6 V18 A7 3 0 0 0 19 18 V6",
        ["cone"] = "M12 3 L5 18 M12 3 L19 18 M5 18 A7 3 0 0 0 19 18 A7 3 0 0 0 5 18",
        ["sphere"] = "M12 4 a8 8 0 1 0 0.01 0 Z M4 12 A8 3 0 0 0 20 12",
        ["torus"] = "M3 12 A9 5 0 1 0 21 12 A9 5 0 1 0 3 12 M9 12 A3 1.5 0 1 0 15 12 A3 1.5 0 1 0 9 12",
        ["gear"] = "M12 8 a4 4 0 1 0 0.01 0 Z M12 2 V5 M12 19 V22 M2 12 H5 M19 12 H22 M5 5 L7 7 M17 17 L19 19 M19 5 L17 7 M7 17 L5 19",
        ["extrude"] = "M5 18 H15 L19 14 H9 Z M5 18 V10 M15 18 V10 M19 14 V6 M5 10 H15 L19 6 M12 3 V8 M10 5 L12 3 L14 5",
        ["revolve"] = "M12 3 V21 M8 8 C14 8 18 10 18 12 C18 14 14 16 8 16 M6 6 L8 8 L6 10",
        ["union"] = "M9 5 a6 6 0 1 0 0.01 0 Z M15 7 a6 6 0 1 0 0.01 0 Z",
        ["subtract"] = "M9 5 a6 6 0 1 0 0.01 0 Z M14 8 H20 V20 H14 Z",
        ["intersect"] = "M9 6 a6 6 0 1 0 0.01 0 Z M15 6 a6 6 0 1 0 0.01 0 Z",
        ["elevate"] = "M4 20 H20 M12 17 V4 M8 8 L12 4 L16 8",
        ["dist"] = "M3 17 L21 7 M3 21 V13 M21 11 V3",
        ["area"] = "M4 5 H20 V19 H4 Z M4 9 L9 5 M4 14 L14 5 M4 19 L19 5 M9 19 L20 8 M14 19 L20 13",
        ["mass"] = "M7 9 H17 L20 20 H4 Z M10 9 A2 2 0 1 1 14 9",
        ["list"] = "M8 6 H20 M8 12 H20 M8 18 H20 M4 6 H5 M4 12 H5 M4 18 H5",
        ["undo"] = "M9 7 L4 12 L9 17 M4 12 H15 A5 5 0 0 1 15 22",
        ["redo"] = "M15 7 L20 12 L15 17 M20 12 H9 A5 5 0 0 0 9 22",
        ["plan"] = "M3 3 H21 V21 H3 Z M3 9 H21 M3 15 H21 M9 3 V21 M15 3 V21",
        ["split"] = "M3 4 H21 V20 H3 Z M12 4 V20",
        ["cube3d"] = "M4 8 L12 4 L20 8 V16 L12 20 L4 16 Z M4 8 L12 12 L20 8 M12 12 V20",
        ["zoom"] = "M10 4 a6 6 0 1 0 0.01 0 Z M14.5 14.5 L20 20 M7 10 H13 M10 7 V13",
        ["ortho"] = "M5 19 V5 M5 19 H19 M5 5 L8 8 M5 5 L2 8 M19 19 L16 16 M19 19 L16 22",
        ["grid"] = "M4 4 H20 V20 H4 Z M4 9.3 H20 M4 14.6 H20 M9.3 4 V20 M14.6 4 V20",
        ["osnap"] = "M7 7 H17 V17 H7 Z M12 3 V7 M12 17 V21 M3 12 H7 M17 12 H21",
        ["ai"] = "M12 3 L14 9 L20 11 L14 13 L12 19 L10 13 L4 11 L10 9 Z M19 3 L19.8 5.2 L22 6 L19.8 6.8 L19 9 L18.2 6.8 L16 6 L18.2 5.2 Z",
        ["help"] = "M12 3 a9 9 0 1 0 0.01 0 Z M9.5 9 A2.5 2.5 0 1 1 12 11.5 V13.5 M12 17 V17.5",
        ["new"] = "M6 3 H14 L19 8 V21 H6 Z M14 3 V8 H19 M12.5 11 V18 M9 14.5 H16",
        ["open"] = "M3 7 H9 L11 9 H21 V19 H3 Z M3 11 H21",
        ["save"] = "M5 3 H17 L21 7 V21 H3 V5 Z M7 3 V9 H16 V3 M7 21 V14 H17 V21",
        ["export"] = "M12 3 V15 M7 8 L12 3 L17 8 M5 13 V21 H19 V13",
        ["trim"] = "M3 12 H9 M15 12 H21 M12 3 V21 M9 9 L15 15 M15 9 L9 15",
        ["extend"] = "M20 3 V21 M3 12 H12 M12 12 H17 M14 9 L17 12 L14 15",
        ["fillet"] = "M4 20 V12 A8 8 0 0 1 12 4 H20",
        ["chamfer"] = "M4 20 V11 L11 4 H20",
        ["hatch"] = "M4 4 H20 V20 H4 Z M4 10 L10 4 M4 16 L16 4 M8 20 L20 8 M14 20 L20 14",
        ["block"] = "M4 4 H11 V11 H4 Z M13 4 H20 V11 H13 Z M4 13 H11 V20 H4 Z M13 13 H20 V20 H13 Z",
        ["insert"] = "M12 3 V14 M8 10 L12 14 L16 10 M5 15 H19 V21 H5 Z",
        ["plot"] = "M6 9 V3 H18 V9 M6 17 H4 V9 H20 V17 H18 M6 14 H18 V21 H6 Z",
    };
}
