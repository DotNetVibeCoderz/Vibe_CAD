using System.Globalization;
using System.Text.RegularExpressions;
using Gravicode.CAD.Parametric;

namespace Gravicode.CAD.AI;

/// <summary>
/// Offline, deterministic text-to-CAD for common parts (English and Bahasa Indonesia keywords):
/// housings/enclosures, plates, brackets, flanges, gears, tubes, washers, nuts, shafts, cylinders and spheres,
/// with dimensions ("120x80x40 mm", "diameter 40", "tebal 5") and hole counts ("four mounting holes", "4 lubang").
/// </summary>
public static partial class RuleBasedCadInterpreter
{
    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["eight"] = 8, ["ten"] = 10, ["twelve"] = 12,
        ["satu"] = 1, ["dua"] = 2, ["tiga"] = 3, ["empat"] = 4, ["lima"] = 5, ["enam"] = 6, ["delapan"] = 8, ["sepuluh"] = 10, ["dua belas"] = 12,
    };

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:mm)?\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*(?:mm)?\s*(?:[x×*]\s*(\d+(?:[.,]\d+)?))?")]
    private static partial Regex DimsRegex();

    public static PartDefinition Interpret(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        bool Has(params string[] keys) => keys.Any(k => p.Contains(k));
        double? Num(params string[] keys)
        {
            foreach (var k in keys)
            {
                var m = Regex.Match(p, Regex.Escape(k) + @"\s*(?:of|=|:|sebesar|ukuran)?\s*(\d+(?:[.,]\d+)?)");
                if (m.Success) return D(m.Groups[1].Value);
                m = Regex.Match(p, @"(\d+(?:[.,]\d+)?)\s*(?:mm)?\s*" + Regex.Escape(k));
                if (m.Success) return D(m.Groups[1].Value);
            }
            return null;
        }
        int? Count(params string[] nouns)
        {
            foreach (var n in nouns)
            {
                var m = Regex.Match(p, @"(\d+|" + string.Join("|", Words.Keys.Select(Regex.Escape)) + @")\s+(?:[a-z\-]+\s+){0,2}" + Regex.Escape(n));
                if (m.Success) return int.TryParse(m.Groups[1].Value, out var v) ? v : Words[m.Groups[1].Value];
                // Indonesian order: "lubang ... 4" / "4 buah lubang"
                m = Regex.Match(p, Regex.Escape(n) + @"\s+(?:[a-z\-]+\s+){0,2}(\d+|" + string.Join("|", Words.Keys.Select(Regex.Escape)) + @")\b");
                if (m.Success) return int.TryParse(m.Groups[1].Value, out var v2) ? v2 : Words[m.Groups[1].Value];
            }
            return null;
        }

        var dims = DimsRegex().Match(p);
        double? dw = dims.Success ? D(dims.Groups[1].Value) : null, dd = dims.Success ? D(dims.Groups[2].Value) : null;
        double? dh = dims.Success && dims.Groups[3].Success ? D(dims.Groups[3].Value) : null;
        int holes = Count("mounting hole", "hole", "lubang", "bolt", "baut") ?? (Has("hole", "lubang") ? 4 : 0);
        double holeD = Num("hole diameter", "diameter lubang", "m") is { } hd && hd < 30 ? hd : 6.5;
        if (Regex.Match(p, @"\bm(\d{1,2})\b") is { Success: true } mm) holeD = int.Parse(mm.Groups[1].Value) + 0.5;

        var def = new PartDefinition { Description = $"Generated offline from: \"{prompt.Trim()}\"" };
        var f = def.Features;
        var par = def.Parameters;

        bool housing = Has("housing", "enclosure", "box", "case", "casing", "rumah", "kotak", "wadah");
        if (!housing && Has("gear", "roda gigi", "gigi"))
        {
            def.Name = "SpurGear";
            par["module"] = Num("module", "modul") ?? 2;
            par["teeth"] = Num("teeth", "gigi") is { } t && t >= 6 ? Math.Round(t) : Count("teeth", "gigi") ?? 24;
            par["thickness"] = Num("thickness", "thick", "tebal", "face width") ?? 10;
            par["bore"] = Num("bore", "shaft", "poros") ?? 8;
            f.Add(new GearFeature { Module = "module", Teeth = "teeth", Thickness = "thickness", Bore = "bore", Color = "#D4A84B" });
            if (Has("hub")) f.Add(new TubeFeature { OuterRadius = "bore", InnerRadius = "bore/2", Height = "thickness*0.6", Z = "thickness", Color = "#D4A84B" });
        }
        else if (Has("flange", "flens"))
        {
            def.Name = "Flange";
            par["od"] = Num("outer diameter", "od", "diameter") ?? 120;
            par["id"] = Num("inner diameter", "bore", "id") ?? Math.Round((double)Num("outer diameter", "od", "diameter").GetValueOrDefault(120) / 3);
            par["t"] = Num("thickness", "thick", "tebal") ?? 12;
            par["hub_h"] = Num("hub height") ?? 20;
            par["bc"] = "(od + id) / 2 + 2";
            f.Add(new CylinderFeature { Diameter = "od", Height = "t" });
            f.Add(new CylinderFeature { Diameter = "id + 16", Height = "hub_h", Z = "t" });
            f.Add(new HoleFeature { Diameter = "id", Z = "t + hub_h" });
            f.Add(new PatternFeature { Kind = "circular", Count = holes > 0 ? holes : 6, Source = new HoleFeature { Diameter = holeD > 0 ? holeD : 10, X = "bc/2", Z = "t" } });
        }
        else if (housing)
        {
            def.Name = Has("gearbox", "gear box") ? "GearboxHousing" : "Housing";
            par["w"] = dw ?? Num("width", "lebar") ?? 140;
            par["d"] = dd ?? Num("depth", "panjang") ?? 100;
            par["h"] = dh ?? Num("height", "tinggi") ?? 70;
            par["wall"] = Num("wall", "dinding") ?? 4;
            par["flange"] = 14;
            par["ft"] = "wall * 2";
            f.Add(new BoxFeature { Width = "w", Depth = "d", Height = "h", Id = "shell", Color = "#9AA7B4" });
            f.Add(new BoxFeature { Width = "w - 2*wall", Depth = "d - 2*wall", Height = "h", Z = "wall", Operation = FeatureOperation.Subtract, Id = "cavity" });
            // mounting flange around the base
            f.Add(new BoxFeature { Width = "w + 2*flange", Depth = "d + 2*flange", Height = "ft", Id = "base_flange" });
            f.Add(new BoxFeature { Width = "w - 2*wall", Depth = "d - 2*wall", Height = "ft", Z = "wall", Operation = FeatureOperation.Subtract, Id = "flange_clear" });
            int n = holes > 0 ? holes : 4;
            var pts = PerimeterHoles(n, "w/2 + flange/2", "d/2 + flange/2");
            f.Add(new HoleFeature { Diameter = holeD, Z = "ft", Positions = pts, Id = "mounting_holes" });
            if (def.Name == "GearboxHousing")
            {
                par["bearing"] = Num("bearing") ?? 32;
                f.Add(new CylinderFeature { Diameter = "bearing + 2*wall*2", Height = "wall*3", Axis = Axis.Y, Anchor = Anchor.Center, X = "-w/5", Z = "h*0.55", Y = "-d/2", Id = "boss_front" });
                f.Add(new CylinderFeature { Diameter = "bearing", Height = "d + 40", Axis = Axis.Y, Anchor = Anchor.Center, X = "-w/5", Z = "h*0.55", Operation = FeatureOperation.Subtract, Id = "bearing_bore_1" });
                f.Add(new CylinderFeature { Diameter = "bearing*0.8", Height = "d + 40", Axis = Axis.Y, Anchor = Anchor.Center, X = "w/4", Z = "h*0.55", Operation = FeatureOperation.Subtract, Id = "bearing_bore_2" });
            }
        }
        else if (Has("bracket", "braket", "siku", "l-bracket"))
        {
            def.Name = "LBracket";
            par["w"] = dw ?? Num("width", "lebar") ?? 60;
            par["l"] = dd ?? Num("length", "panjang") ?? 80;
            par["h"] = dh ?? Num("height", "tinggi") ?? 60;
            par["t"] = Num("thickness", "thick", "tebal") ?? 6;
            f.Add(new BoxFeature { Width = "w", Depth = "l", Height = "t", Anchor = Anchor.Corner });
            f.Add(new BoxFeature { Width = "w", Depth = "t", Height = "h", Anchor = Anchor.Corner });
            f.Add(new HoleFeature { Diameter = holeD, Z = "t", Positions = [["w*0.3", "l*0.65"], ["w*0.7", "l*0.65"]] });
            f.Add(new CylinderFeature { Diameter = holeD, Height = "t*3", Axis = Axis.Y, Anchor = Anchor.Center, X = "w*0.3", Z = "h*0.65", Operation = FeatureOperation.Subtract });
            f.Add(new CylinderFeature { Diameter = holeD, Height = "t*3", Axis = Axis.Y, Anchor = Anchor.Center, X = "w*0.7", Z = "h*0.65", Operation = FeatureOperation.Subtract });
        }
        else if (Has("washer", "ring", "cincin", "ring"))
        {
            def.Name = "Washer";
            par["od"] = Num("outer diameter", "od", "diameter") ?? 24;
            par["id"] = Num("inner diameter", "id", "bore") ?? holeD;
            par["t"] = Num("thickness", "tebal") ?? 2.5;
            f.Add(new TubeFeature { OuterRadius = "od/2", InnerRadius = "id/2", Height = "t" });
        }
        else if (Has("nut", "mur"))
        {
            def.Name = "HexNut";
            par["af"] = Num("across flats", "af") ?? 13;
            par["t"] = Num("thickness", "tebal") ?? 6.5;
            par["hole"] = holeD > 0 ? holeD - 0.5 : 8;
            f.Add(new ExtrudeFeature { Sketch = new SketchSpec { Shape = "regularPolygon", Sides = 6, Radius = "af / sqrt(3)", Holes = [new SketchSpec { Shape = "circle", Radius = "hole/2" }] }, Height = "t" });
        }
        else if (Has("tube", "pipe", "pipa", "bushing", "sleeve"))
        {
            def.Name = "Tube";
            par["od"] = Num("outer diameter", "od", "diameter") ?? 40;
            par["wall"] = Num("wall", "dinding") ?? 3;
            par["length"] = Num("length", "panjang", "height", "tinggi") ?? 100;
            f.Add(new TubeFeature { OuterRadius = "od/2", InnerRadius = "od/2 - wall", Height = "length" });
        }
        else if (Has("shaft", "poros", "axle"))
        {
            def.Name = "SteppedShaft";
            par["d"] = Num("diameter") ?? 20;
            par["length"] = Num("length", "panjang") ?? 120;
            f.Add(new RevolveFeature { Profile = [[0, 0], ["d/2", 0], ["d/2", "length*0.3"], ["d*0.4", "length*0.3"], ["d*0.4", "length"], [0, "length"]] });
        }
        else if (Has("sphere", "ball", "bola"))
        {
            def.Name = "Sphere";
            par["r"] = (Num("diameter") is { } dia ? dia / 2 : (double?)null) ?? Num("radius", "jari") ?? 25;
            f.Add(new SphereFeature { Radius = "r", Z = "r" });
        }
        else if (Has("cylinder", "silinder", "rod", "batang", "standoff", "spacer"))
        {
            def.Name = "Cylinder";
            par["dia"] = Num("diameter") ?? 30;
            par["h"] = Num("height", "tinggi", "length", "panjang") ?? 50;
            f.Add(new CylinderFeature { Diameter = "dia", Height = "h" });
            if (Has("standoff", "spacer") || holes > 0) f.Add(new HoleFeature { Diameter = holeD, Z = "h" });
        }
        else
        {
            // default: plate with holes
            def.Name = Has("plate", "pelat", "plat") ? "MountingPlate" : "Plate";
            par["w"] = dw ?? Num("width", "lebar") ?? 120;
            par["d"] = dd ?? Num("depth", "panjang") ?? 80;
            par["t"] = dh ?? Num("thickness", "thick", "tebal") ?? 8;
            par["inset"] = 10;
            f.Add(new BoxFeature { Width = "w", Depth = "d", Height = "t" });
            int n = holes > 0 ? holes : 4;
            f.Add(new HoleFeature { Diameter = holeD, Z = "t", Positions = PerimeterHoles(n, "w/2 - inset", "d/2 - inset") });
            if (Has("slot")) f.Add(new ExtrudeFeature { Sketch = new SketchSpec { Shape = "slot", Width = "w/3", Height = holeD }, Height = "t + 2", Z = -1, Operation = FeatureOperation.Subtract });
        }
        return def;
    }

    /// <summary>Hole positions: 4 = corners, otherwise evenly around the rectangle perimeter.</summary>
    private static List<Expr[]> PerimeterHoles(int n, string hx, string hy)
    {
        if (n <= 4)
        {
            Expr[][] corners = [[$"-({hx})", $"-({hy})"], [$"{hx}", $"-({hy})"], [$"{hx}", $"{hy}"], [$"-({hx})", $"{hy}"]];
            return [.. corners.Take(Math.Max(1, n))];
        }
        var pts = new List<Expr[]>();
        int perSide = Math.Max(2, (int)Math.Ceiling(n / 4.0) + 1);
        // corners plus mid-points along the long edges
        pts.AddRange(PerimeterHoles(4, hx, hy));
        int extra = n - 4;
        for (int i = 0; i < extra; i++)
        {
            int k = i / 2 + 1, total = (extra + 1) / 2 + 1;
            string t = $"({hx}) * (2*{k}/{total} - 1)";
            pts.Add([t, i % 2 == 0 ? $"-({hy})" : $"{hy}"]);
        }
        _ = perSide;
        return pts;
    }

    private static double D(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
}
