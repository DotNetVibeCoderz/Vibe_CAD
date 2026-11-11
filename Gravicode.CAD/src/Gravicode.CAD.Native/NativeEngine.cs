using System.Reflection;
using System.Runtime.InteropServices;

namespace Gravicode.CAD.Native;

/// <summary>Entry point for the Rust engine.</summary>
public static class NativeEngine
{
    internal const string Lib = "gravicode_cad_engine";
    private static bool? _available;
    private static string? _error;

    static NativeEngine()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeEngine).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        var rid = RuntimeInformation.RuntimeIdentifier;
        string file = OperatingSystem.IsWindows() ? $"{Lib}.dll" : OperatingSystem.IsMacOS() ? $"lib{Lib}.dylib" : $"lib{Lib}.so";
        string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
        {
            Path.Combine(baseDir, file),
            Path.Combine(baseDir, "runtimes", $"{os}-{arch}", "native", file),
            Path.Combine(baseDir, "runtimes", rid, "native", file),
        })
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var h)) return h;
        return NativeLibrary.TryLoad(file, asm, path, out var h2) ? h2 : IntPtr.Zero;
    }

    /// <summary>True when the native library could be loaded.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available is { } a) return a;
            try { _ = Marshal.PtrToStringUTF8(Interop.gc_engine_version()); _available = true; }
            catch (Exception ex) { _available = false; _error = ex.Message; }
            return _available.Value;
        }
    }

    public static string? LoadError => IsAvailable ? null : _error;
    public static string Version => IsAvailable ? Marshal.PtrToStringUTF8(Interop.gc_engine_version()) ?? "?" : "unavailable";
    public static int Threads => IsAvailable ? Interop.gc_engine_threads() : 1;

    /// <summary>Routes <see cref="MeshKernel.Current"/> through Rust. Returns false (and keeps the managed kernel) if unavailable.</summary>
    public static bool Enable()
    {
        if (!IsAvailable) return false;
        MeshKernel.Current = RustMeshKernel.Instance;
        return true;
    }

    public static void Disable() => MeshKernel.Current = ManagedMeshKernel.Instance;
}

internal static partial class Interop
{
    [LibraryImport(NativeEngine.Lib)] internal static partial IntPtr gc_engine_version();
    [LibraryImport(NativeEngine.Lib)] internal static partial int gc_engine_threads();
    [LibraryImport(NativeEngine.Lib)] internal static unsafe partial int gc_mesh_properties(double* pos, nuint nverts, int* idx, nuint nidx, double* output);
    [LibraryImport(NativeEngine.Lib)] internal static unsafe partial int gc_mesh_vertex_normals(double* pos, nuint nverts, int* idx, nuint nidx, double* outNormals);
    [LibraryImport(NativeEngine.Lib)] internal static unsafe partial int gc_mesh_weld(double* pos, nuint nverts, double tolerance, int* outRemap, nuint* outUnique);
    [LibraryImport(NativeEngine.Lib)] internal static unsafe partial int gc_mesh_slice_z(double* pos, nuint nverts, int* idx, nuint nidx, double z, double* outSegments, nuint capacity, nuint* outCount);
}

/// <summary><see cref="IMeshKernel"/> implemented by the Rust engine (zero-copy: <see cref="Vec3"/> lists are pinned as flat double arrays).</summary>
public sealed unsafe class RustMeshKernel : IMeshKernel
{
    public static readonly RustMeshKernel Instance = new();
    public string Name => "Rust native (" + NativeEngine.Version + ")";

    private static void Check(int code)
    {
        if (code != 0) throw new InvalidOperationException($"gravicode-cad-engine error {code}");
    }

    public MeshProperties ComputeProperties(Mesh mesh)
    {
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var idx = CollectionsMarshal.AsSpan(mesh.Indices);
        double* o = stackalloc double[5];
        fixed (Vec3* pp = pos) fixed (int* ip = idx)
            Check(Interop.gc_mesh_properties((double*)pp, (nuint)pos.Length, ip, (nuint)idx.Length, o));
        return new MeshProperties(o[0], o[1], new Vec3(o[2], o[3], o[4]));
    }

    public void ComputeVertexNormals(Mesh mesh)
    {
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var idx = CollectionsMarshal.AsSpan(mesh.Indices);
        var normals = new Vec3[pos.Length];
        fixed (Vec3* pp = pos) fixed (int* ip = idx) fixed (Vec3* np = normals)
            Check(Interop.gc_mesh_vertex_normals((double*)pp, (nuint)pos.Length, ip, (nuint)idx.Length, (double*)np));
        mesh.Normals = [.. normals];
    }

    public Mesh Weld(Mesh mesh, double tolerance)
    {
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var remap = new int[pos.Length];
        nuint unique;
        fixed (Vec3* pp = pos) fixed (int* rp = remap)
            Check(Interop.gc_mesh_weld((double*)pp, (nuint)pos.Length, tolerance, rp, &unique));
        var r = new Mesh((int)unique, mesh.TriangleCount);
        var placed = new bool[(int)unique];
        for (int i = 0; i < (int)unique; i++) r.Positions.Add(default);
        for (int i = 0; i < remap.Length; i++)
            if (!placed[remap[i]]) { placed[remap[i]] = true; r.Positions[remap[i]] = mesh.Positions[i]; }
        var ix = mesh.Indices;
        for (int i = 0; i + 2 < ix.Count; i += 3)
        {
            int a = remap[ix[i]], b = remap[ix[i + 1]], c = remap[ix[i + 2]];
            if (a != b && b != c && a != c) r.AddTriangle(a, b, c);
        }
        return r;
    }

    public List<(Vec3 A, Vec3 B)> SliceZ(Mesh mesh, double z)
    {
        var pos = CollectionsMarshal.AsSpan(mesh.Positions);
        var idx = CollectionsMarshal.AsSpan(mesh.Indices);
        int cap = Math.Max(1, mesh.TriangleCount);
        var buf = new Vec3[cap * 2];
        nuint count;
        fixed (Vec3* pp = pos) fixed (int* ip = idx) fixed (Vec3* bp = buf)
            Check(Interop.gc_mesh_slice_z((double*)pp, (nuint)pos.Length, ip, (nuint)idx.Length, z, (double*)bp, (nuint)cap, &count));
        var r = new List<(Vec3, Vec3)>((int)count);
        for (int i = 0; i < (int)count; i++) r.Add((buf[2 * i], buf[2 * i + 1]));
        return r;
    }
}
