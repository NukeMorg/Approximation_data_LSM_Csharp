using MathNet.Numerics.LinearAlgebra;

namespace CurseWork.Core.Regression.Regression3D;

public sealed class QuadraticSurfaceRegression : IRegression<SurfaceResult>
{
    // z = a*x² + b*y² + c*x*y + d*x + e*y + f
    private readonly double[] _x, _y, _z;

    public QuadraticSurfaceRegression(double[] x, double[] y, double[] z)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    public SurfaceResult Fit()
    {
        int n = _x.Length;
        var A = Matrix<double>.Build.Dense(n, 6);
        for (int i = 0; i < n; i++)
        {
            double xi = _x[i], yi = _y[i];
            A[i, 0] = xi * xi;
            A[i, 1] = yi * yi;
            A[i, 2] = xi * yi;
            A[i, 3] = xi;
            A[i, 4] = yi;
            A[i, 5] = 1.0;
        }
        var b = Vector<double>.Build.DenseOfArray(_z);
        var coeff = A.TransposeThisAndMultiply(A).Solve(A.TransposeThisAndMultiply(b));

        double[] coeffArray = coeff.ToArray();
        double[] zPred = Predict(_x, _y, coeffArray);
        var metrics = CalculateMetrics(_z, zPred);
        return new SurfaceResult(coeffArray, metrics, zPred);
    }

    public static double[] Predict(double[] x, double[] y, double[] coeff)
    {
        double[] pred = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double xi = x[i], yi = y[i];
            pred[i] = coeff[0] * xi * xi +
                      coeff[1] * yi * yi +
                      coeff[2] * xi * yi +
                      coeff[3] * xi +
                      coeff[4] * yi +
                      coeff[5];
        }
        return pred;
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
        int p = 6;
        double adjR2 = n > p + 1 ? 1 - (1 - r2) * (n - 1) / (n - p - 1) : r2;
        return new RegressionMetrics(mse, adjR2);
    }
}

public record SurfaceResult(double[] Coefficients, RegressionMetrics Metrics, double[] ZPred);