using MathNet.Numerics.LinearAlgebra;

namespace CurseWork.Core.Regression.Regression3D;

public sealed class PlaneRegression : IRegression<PlaneResult>
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _z;

    public PlaneRegression(double[] x, double[] y, double[] z)
    {
        _x = x ?? throw new ArgumentNullException(nameof(x));
        _y = y ?? throw new ArgumentNullException(nameof(y));
        _z = z ?? throw new ArgumentNullException(nameof(z));
        if (x.Length != y.Length || x.Length != z.Length)
            throw new ArgumentException("All arrays must have same length");
    }

    public PlaneResult Fit()
    {
        int n = _x.Length;
        var A = Matrix<double>.Build.Dense(n, 3);
        for (int i = 0; i < n; i++)
        {
            A[i, 0] = _x[i];
            A[i, 1] = _y[i];
            A[i, 2] = 1.0;
        }
        var b = Vector<double>.Build.DenseOfArray(_z);
        var coeff = A.TransposeThisAndMultiply(A).Solve(A.TransposeThisAndMultiply(b));
        double a = coeff[0];
        double bcoef = coeff[1];
        double c = coeff[2];

        var zPred = new double[n];
        for (int i = 0; i < n; i++)
            zPred[i] = a * _x[i] + bcoef * _y[i] + c;

        var metrics = CalculateMetrics(_z, zPred);
        return new PlaneResult(a, bcoef, c, metrics, zPred);
    }

    private static RegressionMetrics CalculateMetrics(double[] zTrue, double[] zPred)
    {
        double ssRes = 0.0, ssTot = 0.0;
        double mean = zTrue.Average();
        for (int i = 0; i < zTrue.Length; i++)
        {
            ssRes += (zTrue[i] - zPred[i]) * (zTrue[i] - zPred[i]);
            ssTot += (zTrue[i] - mean) * (zTrue[i] - mean);
        }
        double mse = ssRes / zTrue.Length;
        double r2 = Math.Abs(ssTot) < 1e-12 ? 0 : 1 - ssRes / ssTot;
        int n = zTrue.Length;
        int p = 3;
        double adjR2 = n > p + 1 ? 1 - (1 - r2) * (n - 1) / (n - p - 1) : r2;
        return new RegressionMetrics(mse, adjR2);
    }
}

public record PlaneResult(double A, double B, double C, RegressionMetrics Metrics, double[] ZPred);