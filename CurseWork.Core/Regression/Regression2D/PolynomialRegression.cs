using MathNet.Numerics.LinearAlgebra;

namespace CurseWork.Core.Regression.Regression2D;

public sealed class PolynomialRegression : IRegression<double[]>
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly int _degree;

    public PolynomialRegression(double[] x, double[] y, int degree)
    {
        _x = x ?? throw new ArgumentNullException(nameof(x));
        _y = y ?? throw new ArgumentNullException(nameof(y));
        if (x.Length != y.Length) throw new ArgumentException("x and y must have same length");
        if (degree < 1) throw new ArgumentOutOfRangeException(nameof(degree));
        _degree = degree;
    }

    public double[] Fit() => Ols();

    public double[] Ols()
    {
        var X = BuildVandermonde(_x, _degree);
        var y = Vector<double>.Build.DenseOfArray(_y);
        var Xt = X.Transpose();
        var A = Xt * X;
        var b = Xt * y;
        return A.Solve(b).ToArray();
    }

    public double[] Wls(double[]? weights)
    {
        var w = weights is null || weights.Length != _y.Length
            ? Enumerable.Repeat(1.0, _y.Length).ToArray()
            : weights.Select(v => double.IsFinite(v) ? v : 1.0).ToArray();

        var X = BuildVandermonde(_x, _degree);
        var y = Vector<double>.Build.DenseOfArray(_y);
        var W = Matrix<double>.Build.DiagonalOfDiagonalVector(Vector<double>.Build.DenseOfArray(w));

        var Xt = X.Transpose();
        var A = Xt * W * X;
        var b = Xt * W * y;
        return A.Solve(b).ToArray();
    }

    public double[] Gls(double[,]? covMatrix)
    {
        var n = _x.Length;
        var C = covMatrix;
        if (C is null || C.GetLength(0) != n || C.GetLength(1) != n)
        {
            C = new double[n, n];
            for (var i = 0; i < n; i++) C[i, i] = 1.0;
        }

        Matrix<double> invC;
        try { invC = Matrix<double>.Build.DenseOfArray(C).Inverse(); }
        catch { invC = Matrix<double>.Build.DenseIdentity(n); }

        var X = BuildVandermonde(_x, _degree);
        var y = Vector<double>.Build.DenseOfArray(_y);
        var Xt = X.Transpose();
        var A = Xt * invC * X;
        var b = Xt * invC * y;
        return A.Solve(b).ToArray();
    }

    public double[] Predict(double[] xVals, double[] coeffs) => PolyPredict(xVals, coeffs);

    public static double[] PolyPredict(double[] x, double[] coeffs)
    {
        var yPred = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var xi = x[i];
            var xPow = 1.0;
            var acc = 0.0;
            for (var j = 0; j < coeffs.Length; j++)
            {
                acc += coeffs[j] * xPow;
                xPow *= xi;
            }
            yPred[i] = acc;
        }
        return yPred;
    }

    public RegressionMetrics CalculateMetrics(double[] yTrue, double[] yPred)
    {
        var n = yTrue.Length;
        if (n != yPred.Length) throw new ArgumentException("yTrue and yPred must have same length");

        var mean = yTrue.Average();
        var ssRes = 0.0;
        var ssTot = 0.0;
        for (var i = 0; i < n; i++)
        {
            var e = yTrue[i] - yPred[i];
            ssRes += e * e;
            var d = yTrue[i] - mean;
            ssTot += d * d;
        }
        var mse = ssRes / n;
        var r2 = ssTot == 0.0 ? 0.0 : 1.0 - ssRes / ssTot;
        var p = _degree + 1;
        var adjR2 = (n > p + 1) ? 1.0 - (1.0 - r2) * (n - 1.0) / (n - p - 1.0) : r2;
        return new RegressionMetrics(mse, adjR2);
    }

    private static Matrix<double> BuildVandermonde(double[] x, int degree)
    {
        var n = x.Length;
        var m = degree + 1;
        var X = Matrix<double>.Build.Dense(n, m);
        for (var i = 0; i < n; i++)
        {
            var xi = x[i];
            var xPow = 1.0;
            for (var j = 0; j < m; j++)
            {
                X[i, j] = xPow;
                xPow *= xi;
            }
        }
        return X;
    }
}