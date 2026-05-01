using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using CurseWork.Core.Regression;
using CurseWork.Core.Regression.Regression3D;

namespace CurseWork.ViewModels
{
    public class Main3DViewModel : INotifyPropertyChanged
    {
        private double[]? _x, _y, _z;
        private double[]? _zPred;

        private string _regressionEquation = string.Empty;
        private Color _surfaceColor = Colors.OrangeRed;
        private string? _surfaceType = "Плоскость";
        private double _mse, _rmse, _r2, _adjR2;

        public Color SurfaceColor
        {
            get => _surfaceColor;
            set
            {
                if (Set(ref _surfaceColor, value))
                    RequestVisualize?.Invoke();
            }
        }

        public string? SurfaceType
        {
            get => _surfaceType;
            set => Set(ref _surfaceType, value);
        }

        public double MSE { get => _mse; set => Set(ref _mse, value); }
        public double RMSE { get => _rmse; set => Set(ref _rmse, value); }
        public double R2 { get => _r2; set => Set(ref _r2, value); }
        public double AdjustedR2 { get => _adjR2; set => Set(ref _adjR2, value); }

        public string RegressionEquation
        {
            get => _regressionEquation;
            set => Set(ref _regressionEquation, value);
        }

        public ObservableCollection<CoefficientItem3D> Coefficients { get; } = new();
        public ObservableCollection<PredictionItem3D> Predictions { get; } = new();

        public event Action? RequestVisualize;
        public ICommand BuildModelCommand { get; }

        public Main3DViewModel()
        {
            BuildModelCommand = new RelayCommand(_ => BuildModel());
        }

        public event Action? ModelBuilt;

        public void SetData(double[] x, double[] y, double[] z)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        public (double[] x, double[] y, double[] z, double[] zPred) GetVisualizationData()
        {
            if (_x == null || _y == null || _z == null || _zPred == null)
                throw new InvalidOperationException("Данные не готовы");
            return (_x, _y, _z, _zPred);
        }

        private void BuildModel()
        {
            if (_x == null || _y == null || _z == null)
                throw new InvalidOperationException("Нет данных для построения 3D модели");

            bool isPlane = SurfaceType?.Contains("Плоскость") == true;

            if (isPlane)
            {
                var reg = new PlaneRegression(_x, _y, _z);
                var res = reg.Fit();
                _zPred = res.ZPred;
                Coefficients.Clear();
                Coefficients.Add(new CoefficientItem3D { Index = 0, Value = res.A });
                Coefficients.Add(new CoefficientItem3D { Index = 1, Value = res.B });
                Coefficients.Add(new CoefficientItem3D { Index = 2, Value = res.C });
                RegressionEquation = $"z = {res.A:F4}·x + {res.B:F4}·y + {res.C:F4}";
                UpdateMetrics(res.Metrics);
            }
            else
            {
                var reg = new QuadraticSurfaceRegression(_x, _y, _z);
                var res = reg.Fit();
                _zPred = res.ZPred;
                Coefficients.Clear();
                for (int i = 0; i < res.Coefficients.Length; i++)
                    Coefficients.Add(new CoefficientItem3D { Index = i, Value = res.Coefficients[i] });
                var c = res.Coefficients;
                RegressionEquation = $"z = {c[0]:F4}·x² + {c[1]:F4}·y² + {c[2]:F4}·x·y + {c[3]:F4}·x + {c[4]:F4}·y + {c[5]:F4}";
                UpdateMetrics(res.Metrics);
            }

            Predictions.Clear();
            for (int i = 0; i < _x.Length; i++)
                Predictions.Add(new PredictionItem3D { X = _x[i], PredictedZ = _zPred?[i] ?? 0 });

            RequestVisualize?.Invoke();
            ModelBuilt?.Invoke();
        }

        private void UpdateMetrics(RegressionMetrics m)
        {
            MSE = m.Mse;
            RMSE = Math.Sqrt(m.Mse);
            AdjustedR2 = m.AdjustedR2;
            R2 = m.AdjustedR2;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(name);
            return true;
        }
    }

    public class CoefficientItem3D
    {
        public int Index { get; set; }
        public double Value { get; set; }
    }

    public class PredictionItem3D
    {
        public double X { get; set; }
        public double PredictedZ { get; set; }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}