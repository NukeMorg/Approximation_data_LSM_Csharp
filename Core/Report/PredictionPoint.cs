namespace CurseWork.Core.Report
{
    public readonly struct PredictionPoint
    {
        public double X { get; init; }
        public double Y { get; init; }     // В 2D это предсказанное Y, в 3D – предсказанное Z
        public double? Z { get; init; }    // Только для 3D, может быть null
    }
}