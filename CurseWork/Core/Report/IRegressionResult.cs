using CurseWork.Core.Regression;

namespace CurseWork.Core.Report
{
    public interface IRegressionResult
    {
        /// <summary> Строка вида "y = ..." или "z = ..." </summary>
        string Equation { get; }
        /// <summary> Коэффициенты модели (все, включая свободный член) </summary>
        IReadOnlyList<double> Coefficients { get; }
        /// <summary> Основные метрики качества </summary>
        RegressionMetrics Metrics { get; }
        /// <summary> Предсказанные значения (универсальная точка) </summary>
        IReadOnlyList<PredictionPoint> Predictions { get; }
        /// <summary> PNG-изображение графика (для 2D) или null </summary>
        byte[]? GetGraphImage();
        /// <summary> true, если это 3D-модель </summary>
        bool Is3D { get; }
    }
}