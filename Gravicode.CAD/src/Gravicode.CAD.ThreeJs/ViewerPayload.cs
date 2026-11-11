using System.Runtime.InteropServices;
using Gravicode.CAD.Topology;

namespace Gravicode.CAD.ThreeJs;

/// <summary>Body description sent to JavaScript; arrays live in one binary buffer referenced by offsets (bytes).</summary>
public sealed record ViewerBody(string Name, string Color, double Opacity, double Metallic, double Roughness,
    int PositionsOffset, int PositionsCount, int NormalsOffset, int IndicesOffset, int IndicesCount, int EdgesOffset, int EdgesCount);

public sealed record ViewerWire(string Color, int Offset, int Count);

public sealed record ViewerScene(string Name, string Units, ViewerBody[] Bodies, ViewerWire[] Wires, double[] Min, double[] Max);

/// <summary>Packs a <see cref="CadModel"/> into a compact binary form for the three.js viewer.</summary>
public static class ViewerPayload
{
    public static (ViewerScene Scene, byte[] Buffer) Build(CadModel model, bool edges = true, double edgeAngle = 30)
    {
        using var ms = new MemoryStream();
        int Write<T>(T[] data) where T : unmanaged
        {
            while (ms.Length % 4 != 0) ms.WriteByte(0);
            int off = (int)ms.Length;
            ms.Write(MemoryMarshal.AsBytes(data.AsSpan()));
            return off;
        }

        var bodies = new List<ViewerBody>();
        foreach (var b in model.Bodies.Where(b => b.Visible && !b.Mesh.IsEmpty))
        {
            var sm = ShadedMesh.FromMesh(b.Mesh, 35);
            int po = Write(sm.Positions), no = Write(sm.Normals), io = Write(sm.Indices);
            int eo = 0, ec = 0;
            if (edges)
            {
                var fe = new HalfEdgeMesh(b.Mesh).FeatureEdges(edgeAngle);
                var arr = new float[fe.Count * 6];
                for (int i = 0; i < fe.Count; i++)
                {
                    var (p, q) = fe[i];
                    arr[i * 6] = (float)p.X; arr[i * 6 + 1] = (float)p.Y; arr[i * 6 + 2] = (float)p.Z;
                    arr[i * 6 + 3] = (float)q.X; arr[i * 6 + 4] = (float)q.Y; arr[i * 6 + 5] = (float)q.Z;
                }
                eo = Write(arr); ec = arr.Length;
            }
            bodies.Add(new ViewerBody(b.Name, b.Color.ToHex(), b.Color.A, b.Metallic, b.Roughness, po, sm.Positions.Length, no, io, sm.Indices.Length, eo, ec));
        }

        var wires = new List<ViewerWire>();
        // 3D wires and 2D drawing entities are shown as line segments
        foreach (var w in model.Wires.Where(w => w.Points.Count >= 2))
        {
            var pts = w.Closed ? [.. w.Points, w.Points[0]] : w.Points;
            var arr = new float[(pts.Count - 1) * 6];
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                arr[i * 6] = (float)pts[i].X; arr[i * 6 + 1] = (float)pts[i].Y; arr[i * 6 + 2] = (float)pts[i].Z;
                arr[i * 6 + 3] = (float)pts[i + 1].X; arr[i * 6 + 4] = (float)pts[i + 1].Y; arr[i * 6 + 5] = (float)pts[i + 1].Z;
            }
            wires.Add(new ViewerWire((w.Color ?? CadColor.Orange).ToHex(), Write(arr), arr.Length));
        }
        foreach (var e in model.Drawing)
        {
            var pts = e.Tessellate(0.05);
            if (pts.Count < 2) continue;
            var arr = new float[(pts.Count - 1) * 6];
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                arr[i * 6] = (float)pts[i].X; arr[i * 6 + 1] = (float)pts[i].Y;
                arr[i * 6 + 3] = (float)pts[i + 1].X; arr[i * 6 + 4] = (float)pts[i + 1].Y;
            }
            wires.Add(new ViewerWire((e.Color ?? CadColor.FromHex("#E6EDF3")).ToHex(), Write(arr), arr.Length));
        }

        var bb = model.Bounds;
        if (bb.IsEmpty) bb = new BoundingBox(new Vec3(-50, -50, 0), new Vec3(50, 50, 50));
        var scene = new ViewerScene(model.Name, model.Units.Symbol(), [.. bodies], [.. wires], [bb.Min.X, bb.Min.Y, bb.Min.Z], [bb.Max.X, bb.Max.Y, bb.Max.Z]);
        return (scene, ms.ToArray());
    }
}
