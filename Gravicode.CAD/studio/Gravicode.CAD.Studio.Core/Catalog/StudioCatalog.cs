using Gravicode.CAD.FEM;
using Gravicode.CAD.Geometry;
using Gravicode.CAD.Parametric;
using static Gravicode.CAD.Studio.Catalog.Draft;

namespace Gravicode.CAD.Studio.Catalog;

/// <summary>A starter (template) or finished example (sample) shown on the start screen.</summary>
public sealed record CatalogItem(string Id, string Title, string Category, string Summary, string SummaryId, bool IsTemplate, Func<StudioDocument> Create);

/// <summary>Templates and samples for many industries, all built with the Gravicode.CAD library.</summary>
public static partial class StudioCatalog
{
    public static readonly string[] Categories =
    [
        "Building & Architecture", "Office", "Industry", "Products", "Mechanical", "Automotive", "Aerospace", "Robotics",
        "3D Print", "Games", "Military", "Marine", "Energy", "Electronics", "Medical", "Furniture", "Civil"
    ];

    public static IReadOnlyList<CatalogItem> Templates { get; } =
    [
        new("tpl-blank", "Blank drawing (mm)", "General", "Empty model space with standard layers, metric units and a 10 mm grid.", "Ruang model kosong dengan layer standar, satuan metrik dan grid 10 mm.", true, () => Blank()),
        new("tpl-arch", "Architectural plan 1:50", "Building & Architecture", "A1 sheet, wall/door/window/furniture layers, 500 mm grid.", "Lembar A1, layer dinding/pintu/jendela/furnitur, grid 500 mm.", true, ArchitecturalTemplate),
        new("tpl-mech", "Mechanical part A3", "Mechanical", "A3 sheet with outline, hidden, centre and dimension layers.", "Lembar A3 dengan layer outline, hidden, center dan dimensi.", true, MechanicalTemplate),
        new("tpl-print", "3D print build plate", "3D Print", "220 × 220 mm bed outline, safe-zone and print layers.", "Outline bed 220 × 220 mm, zona aman dan layer cetak.", true, PrintTemplate),
        new("tpl-game", "Game level blockout", "Games", "1 m grid, blockout / props / spawn layers for level greyboxing.", "Grid 1 m, layer blockout / props / spawn untuk greybox level.", true, GameTemplate),
        new("tpl-pcb", "Electronics enclosure", "Electronics", "PCB outline with mounting holes and keep-out zones.", "Outline PCB dengan lubang mounting dan zona larangan.", true, PcbTemplate),
        new("tpl-robot", "Robot work cell", "Robotics", "3 × 3 m cell, safety fence and reach-envelope layers.", "Sel kerja 3 × 3 m, pagar pengaman dan layer jangkauan robot.", true, RobotCellTemplate),
        new("tpl-aero", "Airframe layout", "Aerospace", "Airframe, systems and reference-line layers on an A2 sheet.", "Layer airframe, sistem dan garis referensi pada lembar A2.", true, AeroTemplate),
    ];

    public static IReadOnlyList<CatalogItem> Samples { get; } =
    [
        new("house", "Two-bedroom house", "Building & Architecture", "Floor plan with doors, windows and room labels, extruded into walls, slab and a gable roof.", "Denah dengan pintu, jendela dan label ruang, di-extrude menjadi dinding, pelat lantai dan atap pelana.", false, House),
        new("office", "Open-plan office", "Office", "Desk clusters, meeting room and furniture in plan and 3D.", "Klaster meja, ruang rapat dan furnitur dalam denah dan 3D.", false, Office),
        new("conveyor", "Roller conveyor module", "Industry", "Steel frame, 20 rollers and a drive unit with an elevation drawing.", "Rangka baja, 20 roller dan unit penggerak dengan gambar tampak samping.", false, Conveyor),
        new("speaker", "Bluetooth speaker", "Products", "Shelled housing with a drilled grille and control buttons.", "Casing berongga dengan grille berlubang dan tombol kontrol.", false, Speaker),
        new("gearbox", "Spur gearbox", "Mechanical", "Meshing gear pair on shafts with bearing blocks on a base plate.", "Pasangan roda gigi pada poros dengan dudukan bearing di atas pelat dasar.", false, Gearbox),
        new("flange", "Flange drawing", "Mechanical", "Dimensioned flange drawing with a bolt circle, plus the solid.", "Gambar flens berdimensi dengan lingkaran baut, beserta solidnya.", false, FlangeDrawing),
        new("wheel", "Wheel & brake assembly", "Automotive", "Revolved rim, five spokes, tyre, drilled brake disc and caliper.", "Velg hasil revolve, lima jari-jari, ban, cakram rem berlubang dan kaliper.", false, Wheel),
        new("rocket", "Sounding rocket", "Aerospace", "Revolved airframe with four fins and a NACA 0012 airfoil drawing.", "Badan roket hasil revolve dengan empat sirip dan gambar airfoil NACA 0012.", false, Rocket),
        new("robot", "6-axis robot arm", "Robotics", "Industrial arm with base, joints, links and gripper in a fenced cell.", "Lengan robot industri dengan base, sendi, link dan gripper dalam sel berpagar.", false, Robot),
        new("phone-stand", "Phone stand print plate", "3D Print", "Phone stand with cable slot plus clips laid out on a 220 mm bed.", "Dudukan HP dengan slot kabel dan klip yang ditata di bed 220 mm.", false, PhoneStand),
        new("castle", "Castle tower kit", "Games", "Modular tower with crenellations, door, cone roof and a wall section.", "Menara modular dengan benteng, pintu, atap kerucut dan segmen dinding.", false, Castle),
        new("rugged-case", "Rugged transport case", "Military", "Field equipment case with ribs, latches and carry handle.", "Koper peralatan lapangan dengan rusuk, pengunci dan pegangan.", false, RuggedCase),
        new("hull", "Day-boat hull", "Marine", "Hull lofted through eight stations, with deck and cabin.", "Lambung kapal hasil loft dari delapan station, dengan dek dan kabin.", false, Hull),
        new("turbine", "Wind turbine", "Energy", "Tapered tower, nacelle, hub and three twisted blades.", "Menara meruncing, nacelle, hub dan tiga bilah terpuntir.", false, Turbine),
        new("pcb-box", "IoT device enclosure", "Electronics", "Parametric enclosure with lid around a populated PCB.", "Enclosure parametrik dengan tutup di sekitar PCB berisi komponen.", false, PcbBox),
        new("pill", "Weekly pill organiser", "Medical", "Seven pockets cut from one block, with a lid and day labels.", "Tujuh kantong dari satu blok, dengan tutup dan label hari.", false, PillOrganiser),
        new("dining", "Dining set", "Furniture", "Table and four chairs drawn in plan and modelled in 3D.", "Meja dan empat kursi dalam denah dan model 3D.", false, Dining),
        new("bridge", "Truss footbridge (FEM)", "Civil", "Warren truss analysed with FEM — members coloured by stress.", "Rangka Warren dianalisis dengan FEM — batang diwarnai sesuai tegangan.", false, Bridge),
    ];

    public static CatalogItem? Find(string id) => Templates.Concat(Samples).FirstOrDefault(i => i.Id == id);

    // ===================================================================== templates

    public static StudioDocument Blank(string name = "Drawing1")
    {
        var d = New(name, "General", "Blank drawing", ("Outline", "#E6EDF3"), ("Construction", "#6E7F99"), ("Text", "#F5D76E"));
        d.ClearHistory(); d.IsDirty = false;
        return d;
    }

    private static StudioDocument ArchitecturalTemplate()
    {
        var d = New("Plan", "Building & Architecture", "Architectural plan template 1:50",
            ("Walls", "#E6EDF3"), ("Doors", "#F39C4A"), ("Windows", "#7FD1FF"), ("Furniture", "#9BE3B0"), ("Text", "#F5D76E"));
        Sheet(d, -2000, -2000, 42000, 29700, "Architectural plan", "1:50", 50);
        Done(d); return d;
    }

    private static StudioDocument MechanicalTemplate()
    {
        var d = New("Part", "Mechanical", "Mechanical part template A3",
            ("Outline", "#E6EDF3"), ("Hidden", "#8A94A3"), ("Center", "#F25F5C"), ("Hatch", "#6E7F99"), ("Text", "#F5D76E"));
        Sheet(d, 0, 0, 420, 297, "Part drawing", "1:1");
        Line(d, "Center", 60, 160, 260, 160); Line(d, "Center", 160, 60, 160, 260);
        Done(d); return d;
    }

    private static StudioDocument PrintTemplate()
    {
        var d = New("Print plate", "3D Print", "3D print build plate 220 × 220 mm", ("Parts", "#FF9F43"), ("Bed", "#5F7FA8"), ("Safe zone", "#6E7F99"));
        Rect(d, "Bed", 0, 0, 220, 220); Rect(d, "Safe zone", 5, 5, 210, 210);
        Text(d, "Bed", 4, -8, "BUILD PLATE 220 × 220 mm", 4);
        d.CurrentLayer = "Parts";
        Done(d); return d;
    }

    private static StudioDocument GameTemplate()
    {
        var d = New("Level", "Games", "Game level blockout (1 m grid)", ("Blockout", "#B794F4"), ("Props", "#F6AD55"), ("Spawn", "#68D391"), ("Nav", "#4FD1C5"));
        Rect(d, "Blockout", 0, 0, 40000, 30000);
        Circle(d, "Spawn", 3000, 3000, 600); Text(d, "Spawn", 2500, 3800, "PLAYER START", 400);
        Done(d); return d;
    }

    private static StudioDocument PcbTemplate()
    {
        var d = New("Enclosure", "Electronics", "PCB 100 × 80 mm with M3 mounting holes", ("PCB", "#38A169"), ("Holes", "#E6EDF3"), ("Keep-out", "#F25F5C"));
        Rect(d, "PCB", 0, 0, 100, 80);
        foreach (var (x, y) in new[] { (4.0, 4.0), (96, 4), (96, 76), (4, 76) }) Circle(d, "Holes", x, y, 1.6);
        Rect(d, "Keep-out", 0, 0, 100, 8);
        Done(d); return d;
    }

    private static StudioDocument RobotCellTemplate()
    {
        var d = New("Robot cell", "Robotics", "Robot work cell 3 × 3 m", ("Fence", "#F6E05E"), ("Envelope", "#F25F5C"), ("Equipment", "#7FD1FF"));
        Rect(d, "Fence", -1500, -1500, 3000, 3000);
        Circle(d, "Envelope", 0, 0, 1200); Circle(d, "Envelope", 0, 0, 350);
        Text(d, "Fence", -1450, 1550, "SAFETY FENCE — 1.8 m", 80);
        Done(d); return d;
    }

    private static StudioDocument AeroTemplate()
    {
        var d = New("Airframe", "Aerospace", "Airframe layout on A2", ("Airframe", "#E6EDF3"), ("Systems", "#63B3ED"), ("Reference", "#F25F5C"));
        Sheet(d, -50, -50, 594 * 4, 420 * 4, "Airframe layout", "1:4", 4);
        Line(d, "Reference", 0, 800, 2200, 800); Text(d, "Reference", 10, 820, "FUSELAGE REFERENCE LINE", 20);
        Done(d); return d;
    }

    private static void Done(StudioDocument d) { d.ClearHistory(); d.IsDirty = false; }
}
