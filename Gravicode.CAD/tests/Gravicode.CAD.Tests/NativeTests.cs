using Gravicode.CAD.Native;

namespace Gravicode.CAD.Tests;

public class NativeTests
{
    [Fact]
    public void Rust_kernel_matches_managed()
    {
        Assert.True(NativeEngine.IsAvailable, NativeEngine.LoadError);
        var mesh = Csg.Subtract(Shapes.Box(50, 40, 30), Shapes.Sphere(18, 48, 24));
        var managed = ManagedMeshKernel.Instance;
        var rust = RustMeshKernel.Instance;
        var a = managed.ComputeProperties(mesh); var b = rust.ComputeProperties(mesh);
        Assert.Equal(a.Volume, b.Volume, 6);
        Assert.Equal(a.Area, b.Area, 6);
        Assert.True(a.Centroid.ApproxEquals(b.Centroid, 1e-6));
        Assert.Equal(managed.Weld(mesh.Unwelded(), 1e-6).VertexCount, rust.Weld(mesh.Unwelded(), 1e-6).VertexCount);
        Assert.Equal(managed.SliceZ(mesh, 1.234).Count, rust.SliceZ(mesh, 1.234).Count);
        rust.ComputeVertexNormals(mesh);
        Assert.Equal(mesh.VertexCount, mesh.Normals!.Count);
    }
}
