using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CurseWork.Core.Regression;
using OxyPlot.Wpf;

namespace CurseWork.Core.Report
{
    public sealed class Regression2DResult : IRegressionResult
    {
        private readonly byte[]? _graphImage;

        public string Equation { get; }
        public IReadOnlyList<double> Coefficients { get; }
        public RegressionMetrics Metrics { get; }
        public IReadOnlyList<PredictionPoint> Predictions { get; }
        public bool Is3D => false;

        public Regression2DResult(
            string equation,
            IReadOnlyList<double> coefficients,
            RegressionMetrics metrics,
            double[] xValues,
            double[] yPred,
            OxyPlot.PlotModel? plotModel)
        {
            Equation = equation;
            Coefficients = coefficients;
            Metrics = metrics;

            var points = new List<PredictionPoint>();
            for (int i = 0; i < xValues.Length && i < yPred.Length; i++)
            {
                points.Add(new PredictionPoint { X = xValues[i], Y = yPred[i] });
            }
            Predictions = points;

            if (plotModel != null)
            {
                try
                {
                    var exporter = new PngExporter { Width = 800, Height = 500, Resolution = 96 };
                    using var stream = new MemoryStream();
                    exporter.Export(plotModel, stream);
                    _graphImage = stream.ToArray();
                }
                catch
                {
                    _graphImage = null;
                }
            }
        }

        public byte[]? GetGraphImage() => _graphImage;
    }
}