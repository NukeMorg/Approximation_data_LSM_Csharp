using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CurseWork;

public class MainViewModel : INotifyPropertyChanged
{
    private string _regressionEquation = string.Empty;
    private string _statusText = "Готово";
    private double _mse;
    private double _rmse;
    private double _r2;
    private double _adjustedR2;

    public string RegressionEquation
    {
        get => _regressionEquation;
        set => Set(ref _regressionEquation, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public double MSE
    {
        get => _mse;
        set => Set(ref _mse, value);
    }

    public double RMSE
    {
        get => _rmse;
        set => Set(ref _rmse, value);
    }

    public double R2
    {
        get => _r2;
        set => Set(ref _r2, value);
    }

    public double AdjustedR2
    {
        get => _adjustedR2;
        set => Set(ref _adjustedR2, value);
    }

    public ObservableCollection<CoefficientItem> Coefficients { get; } = new();
    public ObservableCollection<PredictionItem> Predictions { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    public void UpdateCoefficients(IReadOnlyList<double> coeffs)
    {
        Coefficients.Clear();
        for (int i = 0; i < coeffs.Count; i++)
            Coefficients.Add(new CoefficientItem { Index = i, Value = coeffs[i] });
    }

    public void UpdatePredictions(double[] x, double[] yPred)
    {
        Predictions.Clear();
        for (int i = 0; i < x.Length && i < yPred.Length; i++)
            Predictions.Add(new PredictionItem { X = x[i], PredictedY = yPred[i] });
    }

    public void ClearPredictions()
    {
        Predictions.Clear();
    }
}

public class CoefficientItem
{
    public int Index { get; set; }
    public double Value { get; set; }
}

public class PredictionItem
{
    public double X { get; set; }
    public double PredictedY { get; set; }
}