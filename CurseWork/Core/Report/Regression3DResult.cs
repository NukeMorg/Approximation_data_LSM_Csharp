using System;
using System.Collections.Generic;
using System.Linq;
using CurseWork.Core.Regression;

namespace CurseWork.Core.Report
{
    public sealed class Regression3DResult : IRegressionResult
    {
        private readonly byte[]? _graphImage;

        public string Equation { get; }
        public IReadOnlyList<double> Coefficients { get; }
        public RegressionMetrics Metrics { get; }
        public IReadOnlyList<PredictionPoint> Predictions { get; }
        public bool Is3D => true;

        public Regression3DResult(
            string equation,
            IReadOnlyList<double> coefficients,
            RegressionMetrics metrics,
            double[] xValues,
            double[] yValues,
            double[] zPred,
            byte[]? graphImage = null)   // ← новый параметр
        {
            Equation = equation;
            Coefficients = coefficients;
            Metrics = metrics;
            _graphImage = graphImage;

            var points = new List<PredictionPoint>();
            for (int i = 0; i < xValues.Length && i < zPred.Length; i++)
            {
                points.Add(new PredictionPoint
                {
                    X = xValues[i],
                    Y = yValues[i],
                    Z = zPred[i]
                });
            }
            Predictions = points;
        }

        public byte[]? GetGraphImage() => _graphImage;
    }
}