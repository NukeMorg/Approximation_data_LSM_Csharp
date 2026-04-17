using CurseWork.Core.FileIO;
using CurseWork.Core.Regression;
using CurseWork.Core.Saving;
using HelixToolkit.Geometry;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Media3D = System.Windows.Media.Media3D;

namespace CurseWork;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DatasetReader _datasetReader = new();
    private readonly ResultSaver _resultSaver = new();
    private readonly TableSourceSaver _tableSourceSaver = new();

    private DataTable? _table;
    private string? _sourcePath;
    private bool _tableModified;

    private double[]? _x;
    private double[]? _y;
    private double[]? _z;

    private double[]? _weights = null;
    private double[,]? _cov = null;

    private double[]? _coeffs;
    private double[]? _yPred;
    private RegressionMetrics? _metrics;

    private double[]? _surfaceCoeffs;
    private Func<double, double, double>? _surfaceFunc;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel { StatusText = "Готово" };
        DataContext = _vm;
        Plot2D.Model = CreateEmptyPlotModel();
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyPlotMode();
    }

    private void MenuOpen_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();
    private void MenuSave_OnClick(object sender, RoutedEventArgs e) => SaveResults();
    private void MenuExit_OnClick(object sender, RoutedEventArgs e) => Close();

    private void BrowseSource_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();

    private void LoadData_OnClick(object sender, RoutedEventArgs e)
    {
        if (_tableModified) OfferSaveEdits();
        LoadFromPath(SourcePathTextBox.Text);
    }

    private void DegreeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DegreeTextBox is null) return;
        DegreeTextBox.Text = ((int)Math.Round(DegreeSlider.Value)).ToString(CultureInfo.InvariantCulture);
    }

    private void BuildModel_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_table is null) throw new InvalidOperationException("Сначала загрузите данные.");
            LoadArraysFromTable();

            if (_x is null || _y is null) throw new InvalidOperationException("Не удалось выделить X и Y.");

            var degree = ParseDegree();

            if (_z != null && Toggle3D.IsChecked == true)
            {
                // 3D регрессия (поверхность)
                var reg3D = new PolynomialSurfaceRegression(_x, _y, _z, degree);
                _surfaceCoeffs = reg3D.Fit();
                _surfaceFunc = (x, y) => reg3D.Predict(x, y, _surfaceCoeffs);

                var zPred = new double[_z.Length];
                for (int i = 0; i < _z.Length; i++)
                    zPred[i] = _surfaceFunc(_x[i], _y[i]);

                var metrics = CalculateMetrics3D(_z, zPred, degree);
                _metrics = new RegressionMetrics(metrics.MSE, metrics.AdjustedR2);

                _vm.MSE = metrics.MSE;
                _vm.RMSE = Math.Sqrt(metrics.MSE);
                _vm.AdjustedR2 = metrics.AdjustedR2;
                _vm.R2 = metrics.R2;
                _vm.RegressionEquation = FormatSurfaceEquation(_surfaceCoeffs, degree);
                _vm.StatusText = $"OK (3D). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

                Plot3DSurface(_x, _y, _z, _surfaceFunc);
            }
            else
            {
                // 2D регрессия
                var method = ((ComboBoxItem)MethodCombo.SelectedItem).Content?.ToString() ?? "OLS";
                var reg = new PolynomialRegression(_x, _y, degree);
                _coeffs = method switch
                {
                    "WLS" => reg.Wls(_weights),
                    "GLS" => reg.Gls(_cov),
                    _ => reg.Ols()
                };

                _yPred = reg.Predict(_x, _coeffs);
                _metrics = reg.CalculateMetrics(_y, _yPred);

                _vm.MSE = _metrics.Value.Mse;
                _vm.RMSE = Math.Sqrt(_vm.MSE);
                _vm.AdjustedR2 = _metrics.Value.AdjustedR2;
                _vm.R2 = _vm.AdjustedR2;
                _vm.RegressionEquation = FormatEquation(_coeffs);
                _vm.StatusText = $"OK ({method}). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

                Plot2D.Model = Plot2DModel(_x, _y, _yPred);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleMode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (Toggle2D is null || Toggle3D is null || Plot2D is null || Plot3D is null)
            return;

        if (sender == Toggle2D && Toggle2D.IsChecked == true) Toggle3D.IsChecked = false;
        if (sender == Toggle3D && Toggle3D.IsChecked == true) Toggle2D.IsChecked = false;

        ApplyPlotMode();

        if (Toggle3D.IsChecked == true)
        {
            if (_surfaceFunc != null && _x != null && _y != null && _z != null)
                Plot3DSurface(_x, _y, _z, _surfaceFunc);
            else if (_x != null && _y != null && _z != null)
                Plot3DPoints(_x, _y, _z);
        }
    }

    private void ApplyPlotMode()
    {
        if (Toggle2D is null || Toggle3D is null || Plot2D is null || Plot3D is null)
            return;

        var show3d = Toggle3D.IsChecked == true;
        Plot3D.Visibility = show3d ? Visibility.Visible : Visibility.Collapsed;
        Plot2D.Visibility = show3d ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowseAndLoad()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Данные (*.txt;*.csv;*.xlsx;*.db;*.sqlite)|*.txt;*.csv;*.xlsx;*.db;*.sqlite|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;
        SourcePathTextBox.Text = dlg.FileName;
        LoadFromPath(dlg.FileName);
    }

    private void PreviewGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _tableModified = true;
        _vm.StatusText = " Данные изменены. Сохраните изменения или загрузите заново.";
    }

    private void LoadFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Укажите путь к файлу/БД.");

        _sourcePath = path;
        BusyProgress.Visibility = Visibility.Visible;
        try
        {
            var loaded = _datasetReader.LoadAuto(path, previewRows: 200);
            _table = loaded.RawTable;
            _tableModified = false;

            PreviewGrid.ItemsSource = _table.DefaultView;

            PopulateColumnCombos(_table);
            LoadArraysFromTable();

            if (_x is not null && _y is not null)
            {
                Plot2D.Model = Plot2DModel(_x, _y, yPred: null);
                if (_z is not null)
                    Plot3DPoints(_x, _y, _z);
            }

            _vm.StatusText = loaded.Message;
        }
        finally
        {
            BusyProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateColumnCombos(DataTable table)
    {
        var names = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        XColumnCombo.ItemsSource = names;
        YColumnCombo.ItemsSource = names;
        ZColumnCombo.ItemsSource = names;

        XColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(0);
        YColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(1);
        ZColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("z", StringComparison.OrdinalIgnoreCase)) ?? (names.Count >= 3 ? names[2] : null);
    }

    private void LoadArraysFromTable()
    {
        if (_table is null) return;

        if (XColumnCombo.SelectedItem is string xName && YColumnCombo.SelectedItem is string yName
            && _table.Columns.Contains(xName) && _table.Columns.Contains(yName))
        {
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (DataRow row in _table.Rows)
            {
                if (!TryParseCell(row[xName], out var x)) continue;
                if (!TryParseCell(row[yName], out var y)) continue;
                xs.Add(x);
                ys.Add(y);
            }
            _x = xs.ToArray();
            _y = ys.ToArray();

            _z = null;
            if (ZColumnCombo.SelectedItem is string zName && _table.Columns.Contains(zName))
            {
                var zs = new List<double>();
                var ok = true;
                foreach (DataRow row in _table.Rows)
                {
                    if (!TryParseCell(row[zName], out var z))
                    {
                        ok = false;
                        break;
                    }
                    zs.Add(z);
                }
                if (ok && zs.Count == xs.Count && zs.Count >= 2)
                    _z = zs.ToArray();
            }

            if (_z == null && _table.Columns.Count >= 3)
            {
                var numericCols = _table.Columns.Cast<DataColumn>()
                    .Where(c => c.DataType == typeof(double) || _table.Rows.Cast<DataRow>().All(r => TryParseCell(r[c], out _)))
                    .Select(c => c.ColumnName)
                    .ToList();
                if (numericCols.Count >= 3)
                {
                    var autoZ = numericCols[2];
                    if (autoZ != xName && autoZ != yName)
                    {
                        var zs = new List<double>();
                        var ok = true;
                        foreach (DataRow row in _table.Rows)
                        {
                            if (!TryParseCell(row[autoZ], out var val))
                            {
                                ok = false;
                                break;
                            }
                            zs.Add(val);
                        }
                        if (ok && zs.Count == xs.Count)
                            _z = zs.ToArray();
                    }
                }
            }
        }
        else
        {
            var xy = DatasetReader.PrepareXY(_table);
            _x = xy.X;
            _y = xy.Y;
            _z = _table.Columns.Count >= 3 ? DatasetReader.PrepareXYZ(_table).Z : null;
        }
    }

    private static bool TryParseCell(object? value, out double result)
    {
        var raw = value?.ToString() ?? string.Empty;
        raw = raw.Replace(',', '.').Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private int ParseDegree()
    {
        if (int.TryParse(DegreeTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d >= 1)
            return d;
        return 3;
    }

    private void OfferSaveEdits()
    {
        if (_table is null || string.IsNullOrWhiteSpace(_sourcePath)) return;
        var res = MessageBox.Show(this,
            "Данные изменены. Сохранить изменения в источник?",
            "Сохранить?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            _tableSourceSaver.Save(_table, _sourcePath);
            _tableModified = false;
            _vm.StatusText = "Изменения сохранены.";
        }
    }

    private void SaveResults()
    {
        if (_coeffs is null && _surfaceCoeffs is null)
        {
            MessageBox.Show(this, "Сначала постройте модель.", "Нет результата", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "TXT (*.txt)|*.txt|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx|SQLite (*.db;*.sqlite)|*.db;*.sqlite|Все файлы (*.*)|*.*",
            AddExtension = true
        };
        if (dlg.ShowDialog(this) != true) return;

        if (_coeffs != null && _yPred != null && _metrics.HasValue)
        {
            _resultSaver.SaveResults(dlg.FileName, _coeffs, _metrics.Value.Mse, _metrics.Value.AdjustedR2, _yPred, _sourcePath ?? "");
        }
        else if (_surfaceCoeffs != null && _surfaceFunc != null && _x != null && _y != null && _z != null)
        {
            var zPred = new double[_z.Length];
            for (int i = 0; i < _z.Length; i++)
                zPred[i] = _surfaceFunc(_x[i], _y[i]);
            _resultSaver.SaveResults(dlg.FileName, _surfaceCoeffs, _metrics?.Mse ?? 0, _metrics?.AdjustedR2 ?? 0, zPred, _sourcePath ?? "");
        }

        _vm.StatusText = $"Сохранено: {dlg.FileName}";
    }

    private static string FormatEquation(IReadOnlyList<double> coeffs)
    {
        if (coeffs.Count == 0) return "";
        var parts = new List<string>();
        for (var i = 0; i < coeffs.Count; i++)
        {
            var a = coeffs[i];
            var s = a.ToString("G6", CultureInfo.InvariantCulture);
            parts.Add(i switch
            {
                0 => s,
                1 => $"{s}·x",
                _ => $"{s}·x^{i}"
            });
        }
        return "y = " + string.Join(" + ", parts);
    }

    private string FormatSurfaceEquation(double[] coeffs, int degree)
    {
        var parts = new List<string>();
        int idx = 0;
        for (int d = 0; d <= degree; d++)
        {
            for (int k = 0; k <= d; k++)
            {
                int xPow = d - k;
                int yPow = k;
                double a = coeffs[idx++];
                if (Math.Abs(a) < 1e-12) continue;
                string term = a.ToString("G6", CultureInfo.InvariantCulture);
                if (xPow > 0) term += $"·x^{xPow}";
                if (yPow > 0) term += $"·y^{yPow}";
                parts.Add(term);
            }
        }
        return "z = " + string.Join(" + ", parts);
    }

    private (double MSE, double RMSE, double R2, double AdjustedR2) CalculateMetrics3D(double[] zTrue, double[] zPred, int degree)
    {
        int n = zTrue.Length;
        double mean = zTrue.Average();
        double ssRes = 0, ssTot = 0;
        for (int i = 0; i < n; i++)
        {
            double e = zTrue[i] - zPred[i];
            ssRes += e * e;
            double d = zTrue[i] - mean;
            ssTot += d * d;
        }
        double mse = ssRes / n;
        double r2 = ssTot == 0 ? 0 : 1 - ssRes / ssTot;
        int p = (degree + 1) * (degree + 2) / 2;
        double adjR2 = (n > p + 1) ? 1 - (1 - r2) * (n - 1) / (n - p - 1) : r2;
        return (mse, Math.Sqrt(mse), r2, adjR2);
    }

    private static PlotModel CreateEmptyPlotModel()
    {
        var model = new PlotModel { Title = "Аппроксимация" };
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, MajorGridlineStyle = LineStyle.Dash });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, MajorGridlineStyle = LineStyle.Dash });
        return model;
    }

    private static PlotModel Plot2DModel(double[] x, double[] y, double[]? yPred)
    {
        var model = CreateEmptyPlotModel();

        var scatter = new ScatterSeries
        {
            Title = "Данные",
            MarkerType = MarkerType.Circle,
            MarkerSize = 4.0,
            MarkerFill = OxyColor.Parse("#007acc"),
            TrackerFormatString = "X: {2:0.######}\nY: {4:0.######}"
        };
        for (var i = 0; i < x.Length; i++)
            scatter.Points.Add(new ScatterPoint(x[i], y[i]));
        model.Series.Add(scatter);

        if (yPred is not null && yPred.Length == x.Length)
        {
            var line = new LineSeries { Title = "Модель", Color = OxyColor.Parse("#e74c3c"), StrokeThickness = 2 };
            for (var i = 0; i < x.Length; i++)
                line.Points.Add(new DataPoint(x[i], yPred[i]));
            model.Series.Add(line);
        }

        return model;
    }

    private void Plot3DPoints(double[] x, double[] y, double[] z)
    {
        Plot3D.Children.Clear();

        var lightGroup = new Media3D.Model3DGroup();
        lightGroup.Children.Add(new Media3D.AmbientLight(Colors.Gray));
        lightGroup.Children.Add(new Media3D.DirectionalLight(Colors.White, new Media3D.Vector3D(-1, -1, -1)));
        lightGroup.Children.Add(new Media3D.DirectionalLight(Colors.LightGray, new Media3D.Vector3D(1, 1, 1)));
        Plot3D.Children.Add(new Media3D.ModelVisual3D { Content = lightGroup });

        Plot3D.Camera = new Media3D.PerspectiveCamera
        {
            Position = new Media3D.Point3D(5, 5, 5),
            LookDirection = new Media3D.Vector3D(-1, -1, -1),
            UpDirection = new Media3D.Vector3D(0, 0, 1),
            FieldOfView = 60
        };

        var pointsVisual = new PointsVisual3D
        {
            Color = Colors.DodgerBlue,
            Size = 6
        };
        var points = new Media3D.Point3DCollection();
        for (int i = 0; i < x.Length && i < y.Length && i < z.Length; i++)
            points.Add(new Media3D.Point3D(x[i], y[i], z[i]));
        pointsVisual.Points = points;

        var axes = new CoordinateSystemVisual3D
        {
            ArrowLengths = 2.0,
            XAxisColor = Colors.Red,
            YAxisColor = Colors.Green,
            ZAxisColor = Colors.Blue
        };
        Plot3D.Children.Add(axes);
        Plot3D.Children.Add(pointsVisual);
        Plot3D.ZoomExtents();
    }

    private void Plot3DSurface(double[] x, double[] y, double[] z, Func<double, double, double> func)
    {
        Plot3D.Children.Clear();

        // Освещение
        var lightGroup = new Media3D.Model3DGroup();
        lightGroup.Children.Add(new Media3D.AmbientLight(Colors.Gray));
        lightGroup.Children.Add(new Media3D.DirectionalLight(Colors.White, new Media3D.Vector3D(-1, -1, -1)));
        lightGroup.Children.Add(new Media3D.DirectionalLight(Colors.LightGray, new Media3D.Vector3D(1, 1, 1)));
        Plot3D.Children.Add(new Media3D.ModelVisual3D { Content = lightGroup });

        // Камера
        Plot3D.Camera = new Media3D.PerspectiveCamera
        {
            Position = new Media3D.Point3D(5, 5, 5),
            LookDirection = new Media3D.Vector3D(-1, -1, -1),
            UpDirection = new Media3D.Vector3D(0, 0, 1),
            FieldOfView = 60
        };

        // Генерация сетки
        int gridSize = 40;
        double minX = x.Min(), maxX = x.Max();
        double minY = y.Min(), maxY = y.Max();
        double stepX = (maxX - minX) / gridSize;
        double stepY = (maxY - minY) / gridSize;

        var meshBuilder = new MeshBuilder(false, false);
        var points = new System.Numerics.Vector3[gridSize + 1, gridSize + 1];

        for (int i = 0; i <= gridSize; i++)
        {
            double xi = minX + i * stepX;
            for (int j = 0; j <= gridSize; j++)
            {
                double yj = minY + j * stepY;
                double zij = func(xi, yj);
                points[i, j] = new System.Numerics.Vector3((float)xi, (float)yj, (float)zij);
            }
        }

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                var p00 = points[i, j];
                var p10 = points[i + 1, j];
                var p01 = points[i, j + 1];
                var p11 = points[i + 1, j + 1];

                meshBuilder.AddTriangle(p00, p10, p01);
                meshBuilder.AddTriangle(p10, p11, p01);
            }
        }

        var helixMesh = meshBuilder.ToMesh();

        // Конвертация в WPF MeshGeometry3D
        var wpfMesh = new Media3D.MeshGeometry3D();
        var positions = new Media3D.Point3DCollection();
        var normals = new Media3D.Vector3DCollection();
        var triangleIndices = new Int32Collection(); // <-- исправлено

        foreach (var pos in helixMesh.Positions)
            positions.Add(new Media3D.Point3D(pos.X, pos.Y, pos.Z));

        if (helixMesh.Normals != null)
            foreach (var n in helixMesh.Normals)
                normals.Add(new Media3D.Vector3D(n.X, n.Y, n.Z));

        foreach (var tri in helixMesh.TriangleIndices)
            triangleIndices.Add(tri);

        wpfMesh.Positions = positions;
        if (normals.Count > 0) wpfMesh.Normals = normals;
        wpfMesh.TriangleIndices = triangleIndices;

        // Материал
        var material = new Media3D.DiffuseMaterial(new SolidColorBrush(Colors.LightBlue) { Opacity = 0.6 });
        var backMaterial = new Media3D.DiffuseMaterial(new SolidColorBrush(Colors.LightBlue) { Opacity = 0.3 });

        var surfaceModel = new Media3D.GeometryModel3D(wpfMesh, material);
        surfaceModel.BackMaterial = backMaterial;

        var surfaceVisual = new Media3D.ModelVisual3D { Content = surfaceModel };
        Plot3D.Children.Add(surfaceVisual);

        // Точки данных
        var pointsVisual = new PointsVisual3D
        {
            Color = Colors.Red,
            Size = 5
        };
        var pointsCol = new Media3D.Point3DCollection();
        for (int i = 0; i < x.Length; i++)
            pointsCol.Add(new Media3D.Point3D(x[i], y[i], z[i]));
        pointsVisual.Points = pointsCol;
        Plot3D.Children.Add(pointsVisual);

        // Оси
        var axes = new CoordinateSystemVisual3D
        {
            ArrowLengths = 2.0,
            XAxisColor = Colors.Red,
            YAxisColor = Colors.Green,
            ZAxisColor = Colors.Blue
        };
        Plot3D.Children.Add(axes);

        Plot3D.ZoomExtents();
    }
}