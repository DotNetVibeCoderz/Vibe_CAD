namespace Gravicode.CAD.Constraint;

public enum SolveStatus { Solved, OverConstrainedOrInconsistent, NotConverged }

public sealed record SolveResult(SolveStatus Status, int Iterations, double Residual, int Unknowns, int Equations, int Rank)
{
    /// <summary>Remaining degrees of freedom (0 = fully constrained).</summary>
    public int DegreesOfFreedom => Unknowns - Rank;
    public bool Success => Status == SolveStatus.Solved;
    public bool FullyConstrained => Success && DegreesOfFreedom == 0;
    public override string ToString() => $"{Status} in {Iterations} it, |r|={Residual:E2}, DOF={DegreesOfFreedom} ({Equations} eq / {Unknowns} unknowns)";
}

/// <summary>Levenberg–Marquardt solver with finite-difference Jacobian. Fixed points are removed from the unknowns.</summary>
public static class ConstraintSolver
{
    public static SolveResult Solve(Sketch sketch, int maxIterations, double tolerance)
    {
        var values = sketch.Values;
        var free = Enumerable.Range(0, values.Count).Where(i => !sketch.IsFixed(i)).ToArray();
        int n = free.Length;
        int m = sketch.Constraints.Sum(c => c.ResidualCount);
        if (m == 0 || n == 0) return new SolveResult(SolveStatus.Solved, 0, 0, n, m, 0);

        var x = values.ToArray();
        var r = new double[m];
        var rTrial = new double[m];
        var J = new double[m, n];
        double lambda = 1e-3;
        Eval(x, r);
        double err = Norm2(r);
        int it = 0;
        for (; it < maxIterations && err > tolerance * tolerance; it++)
        {
            Jacobian(x, r, J);
            // normal equations A = JᵀJ, g = Jᵀr
            var A = new double[n, n]; var g = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < m; k++) g[i] += J[k, i] * r[k];
                for (int j = i; j < n; j++)
                {
                    double s = 0;
                    for (int k = 0; k < m; k++) s += J[k, i] * J[k, j];
                    A[i, j] = A[j, i] = s;
                }
            }
            bool improved = false;
            for (int tries = 0; tries < 12 && !improved; tries++)
            {
                var M = (double[,])A.Clone();
                for (int i = 0; i < n; i++) M[i, i] += lambda * (1 + A[i, i]);
                var dx = SolveLinear(M, g.Select(v => -v).ToArray());
                if (dx is null) { lambda *= 10; continue; }
                var xt = (double[])x.Clone();
                for (int i = 0; i < n; i++) xt[free[i]] += dx[i];
                Eval(xt, rTrial);
                double e2 = Norm2(rTrial);
                if (e2 < err) { x = xt; Array.Copy(rTrial, r, m); err = e2; lambda = Math.Max(lambda / 10, 1e-12); improved = true; }
                else lambda *= 10;
            }
            if (!improved) break;
        }

        for (int i = 0; i < x.Length; i++) values[i] = x[i];
        Jacobian(x, r, J);
        int rank = Rank(J, m, n);
        double res = Math.Sqrt(err);
        var status = res <= Math.Max(tolerance * 10, 1e-8) ? SolveStatus.Solved
                   : it >= maxIterations ? SolveStatus.NotConverged : SolveStatus.OverConstrainedOrInconsistent;
        return new SolveResult(status, it, res, n, m, rank);

        void Eval(double[] v, double[] outR)
        {
            int o = 0;
            foreach (var c in sketch.Constraints) { c.Residuals(v, outR.AsSpan(o, c.ResidualCount)); o += c.ResidualCount; }
        }

        void Jacobian(double[] v, double[] r0, double[,] jac)
        {
            var tmp = new double[m];
            var tmp2 = new double[m];
            for (int j = 0; j < n; j++)
            {
                int idx = free[j];
                double h = 1e-7 * Math.Max(1, Math.Abs(v[idx]));
                double old = v[idx];
                v[idx] = old + h; Eval(v, tmp);
                v[idx] = old - h; Eval(v, tmp2);
                v[idx] = old;
                for (int k = 0; k < m; k++) jac[k, j] = (tmp[k] - tmp2[k]) / (2 * h);
            }
        }
    }

    private static double Norm2(double[] r) { double s = 0; foreach (var v in r) s += v * v; return s; }

    /// <summary>Gaussian elimination with partial pivoting; null when singular.</summary>
    private static double[]? SolveLinear(double[,] A, double[] b)
    {
        int n = b.Length;
        for (int c = 0; c < n; c++)
        {
            int piv = c; double best = Math.Abs(A[c, c]);
            for (int r = c + 1; r < n; r++) if (Math.Abs(A[r, c]) > best) { best = Math.Abs(A[r, c]); piv = r; }
            if (best < 1e-300) return null;
            if (piv != c)
            {
                for (int k = 0; k < n; k++) (A[c, k], A[piv, k]) = (A[piv, k], A[c, k]);
                (b[c], b[piv]) = (b[piv], b[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = A[r, c] / A[c, c];
                if (f == 0) continue;
                for (int k = c; k < n; k++) A[r, k] -= f * A[c, k];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int k = r + 1; k < n; k++) s -= A[r, k] * x[k];
            x[r] = s / A[r, r];
        }
        return x;
    }

    /// <summary>Numerical rank by Gaussian elimination with relative threshold.</summary>
    private static int Rank(double[,] J, int m, int n)
    {
        var a = (double[,])J.Clone();
        double maxAbs = 0;
        foreach (var v in a) maxAbs = Math.Max(maxAbs, Math.Abs(v));
        double eps = 1e-8 * Math.Max(1, maxAbs);
        int rank = 0;
        var rowUsed = new bool[m];
        for (int c = 0; c < n; c++)
        {
            int piv = -1; double best = eps;
            for (int r = 0; r < m; r++) if (!rowUsed[r] && Math.Abs(a[r, c]) > best) { best = Math.Abs(a[r, c]); piv = r; }
            if (piv < 0) continue;
            rowUsed[piv] = true; rank++;
            for (int r = 0; r < m; r++)
            {
                if (r == piv) continue;
                double f = a[r, c] / a[piv, c];
                if (f == 0) continue;
                for (int k = c; k < n; k++) a[r, k] -= f * a[piv, k];
            }
        }
        return rank;
    }
}
