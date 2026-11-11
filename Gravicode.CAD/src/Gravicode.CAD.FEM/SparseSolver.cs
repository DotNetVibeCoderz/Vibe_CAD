namespace Gravicode.CAD.FEM;

/// <summary>Receives matrix contributions during assembly.</summary>
public interface IMatrixSink { void Add(int row, int col, double value); }

/// <summary>Assembles a sparse matrix from triplets, then compresses to CSR.</summary>
public sealed class SparseBuilder(int n) : IMatrixSink
{
    private readonly Dictionary<long, double> _entries = new(n * 16);
    public int Size { get; } = n;

    public void Add(int row, int col, double value)
    {
        if (value == 0) return;
        long key = (long)row * Size + col;
        _entries[key] = _entries.GetValueOrDefault(key) + value;
    }

    public CsrMatrix Build()
    {
        var keys = _entries.Keys.ToArray();
        Array.Sort(keys);
        var rowPtr = new int[Size + 1];
        var cols = new int[keys.Length];
        var vals = new double[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            int r = (int)(keys[i] / Size);
            cols[i] = (int)(keys[i] % Size);
            vals[i] = _entries[keys[i]];
            rowPtr[r + 1]++;
        }
        for (int r = 0; r < Size; r++) rowPtr[r + 1] += rowPtr[r];
        return new CsrMatrix(Size, rowPtr, cols, vals);
    }
}

/// <summary>Compressed sparse row matrix.</summary>
public sealed class CsrMatrix(int n, int[] rowPtr, int[] cols, double[] vals)
{
    public int Size { get; } = n;
    public int NonZeros => vals.Length;

    public void Multiply(ReadOnlySpan<double> x, Span<double> y)
    {
        for (int r = 0; r < Size; r++)
        {
            double s = 0;
            for (int k = rowPtr[r]; k < rowPtr[r + 1]; k++) s += vals[k] * x[cols[k]];
            y[r] = s;
        }
    }

    public double Diagonal(int r)
    {
        for (int k = rowPtr[r]; k < rowPtr[r + 1]; k++) if (cols[k] == r) return vals[k];
        return 0;
    }

    /// <summary>Jacobi-preconditioned conjugate gradient for symmetric positive-definite systems.</summary>
    public (double[] X, int Iterations, double Residual) SolveCg(double[] b, double tolerance = 1e-10, int maxIterations = 0)
    {
        int n = Size;
        if (maxIterations <= 0) maxIterations = Math.Max(1000, n * 4);
        var x = new double[n]; var r = (double[])b.Clone(); var z = new double[n]; var p = new double[n]; var ap = new double[n];
        var invD = new double[n];
        for (int i = 0; i < n; i++) { var d = Diagonal(i); invD[i] = Math.Abs(d) > 1e-300 ? 1 / d : 1; }
        for (int i = 0; i < n; i++) z[i] = r[i] * invD[i];
        Array.Copy(z, p, n);
        double rz = Dot(r, z), bnorm = Math.Sqrt(Dot(b, b));
        if (bnorm == 0) return (x, 0, 0);
        int it = 0; double res = 1;
        for (; it < maxIterations; it++)
        {
            Multiply(p, ap);
            double alpha = rz / Dot(p, ap);
            for (int i = 0; i < n; i++) { x[i] += alpha * p[i]; r[i] -= alpha * ap[i]; }
            res = Math.Sqrt(Dot(r, r)) / bnorm;
            if (res < tolerance) break;
            for (int i = 0; i < n; i++) z[i] = r[i] * invD[i];
            double rzNew = Dot(r, z);
            double beta = rzNew / rz;
            rz = rzNew;
            for (int i = 0; i < n; i++) p[i] = z[i] + beta * p[i];
        }
        return (x, it + 1, res);

        static double Dot(double[] a, double[] b) { double s = 0; for (int i = 0; i < a.Length; i++) s += a[i] * b[i]; return s; }
    }
}

/// <summary>Solves K u = f with prescribed values on some DOFs (symmetric elimination).</summary>
public static class LinearSystem
{
    public sealed record Solution(double[] U, double[] Reactions, int Iterations, double Residual);

    public static Solution Solve(int ndof, Action<IMatrixSink> assemble, double[] loads, IReadOnlyDictionary<int, double> prescribed, double tolerance = 1e-10)
    {
        var fullB = new SparseBuilder(ndof);
        assemble(fullB);
        var K = fullB.Build();

        var uc = new double[ndof];
        foreach (var (d, v) in prescribed) uc[d] = v;
        var kuc = new double[ndof];
        K.Multiply(uc, kuc);

        var redB = new SparseBuilder(ndof);
        assemble(new Filter(redB, prescribed));
        var f = new double[ndof];
        for (int i = 0; i < ndof; i++) f[i] = prescribed.ContainsKey(i) ? 0 : loads[i] - kuc[i];
        foreach (var (d, v) in prescribed) { redB.Add(d, d, 1); f[d] = v; }
        // DOFs with no stiffness at all (e.g. unused nodes) get a unit diagonal to stay SPD
        var Kred = redB.Build();
        bool patched = false;
        for (int i = 0; i < ndof; i++) if (Kred.Diagonal(i) == 0) { redB.Add(i, i, 1); f[i] = 0; patched = true; }
        if (patched) Kred = redB.Build();
        var (u, it, res) = Kred.SolveCg(f, tolerance);
        foreach (var (d, v) in prescribed) u[d] = v;
        var ku = new double[ndof];
        K.Multiply(u, ku);
        var reactions = new double[ndof];
        for (int i = 0; i < ndof; i++) reactions[i] = ku[i] - loads[i];
        return new Solution(u, reactions, it, res);
    }

    private sealed class Filter(IMatrixSink inner, IReadOnlyDictionary<int, double> fixedDofs) : IMatrixSink
    {
        public void Add(int r, int c, double v) { if (!fixedDofs.ContainsKey(r) && !fixedDofs.ContainsKey(c)) inner.Add(r, c, v); }
    }
}
