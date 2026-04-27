using CurseWork.Core.FileIO;
using CurseWork.Core.Regression;
using CurseWork.Core.Regression.Regression2D;
using CurseWork.Core.Regression.Regression3D;
using CurseWork.Core.Saving;
using CurseWork.ViewModels;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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

    private double[]? _weights;
    private double[,]? _cov;

    private double[]? _coeffs;
    private double[]? _yPred;
    private RegressionMetrics? _metrics;

    // 3D данные и ViewModel
    public Main3DViewModel ViewModel3D { get; } = new();
    private double[]? _x3d, _y3d, _z3d;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel { StatusText = "Готово" };
        DataContext = _vm;
        // Для 3D-вкладки DataContext уже задан в XAML, но продублируем для надёжности
        var tab3D = (TabItem)this.FindName("Tab3D");
        if (tab3D != null) tab3D.DataContext = ViewModel3D;

        Plot2D.Model = CreateEmptyPlotModel();
        ApplyGridVisibility();

        DegreeSlider.ValueChanged += DegreeSlider_ValueChanged;
        DegreeTextBox.TextChanged += DegreeTextBox_TextChanged;
        DegreeSlider.Value = 3;
        DegreeTextBox.Text = "3";
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) { }

    private void MenuOpen_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();
    private void MenuLoadModel_OnClick(object sender, RoutedEventArgs e) => ImportModel();
    private void MenuSave_OnClick(object sender, RoutedEventArgs e) => SaveResults();
    private void MenuExit_OnClick(object sender, RoutedEventArgs e) => Close();
    private void Plot2D_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Plot2D.ResetAllAxes();

    private void BrowseSource_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();

    private void LoadData_OnClick(object sender, RoutedEventArgs e)
    {
        if (_tableModified) OfferSaveEdits();
        LoadFromPath(SourcePathTextBox.Text);
    }

    private void DegreeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DegreeTextBox == null) return;
        int newValue = (int)Math.Round(e.NewValue);
        if (DegreeTextBox.Text != newValue.ToString())
            DegreeTextBox.Text = newValue.ToString();
    }

    private void DegreeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DegreeSlider == null) return;
        if (int.TryParse(DegreeTextBox.Text, out int degree) && degree >= 1)
        {
            if (Math.Abs(DegreeSlider.Value - degree) > 0.1)
                DegreeSlider.Value = degree;
        }
    }

    private int ParseDegree() =>
        int.TryParse(DegreeTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 1 ? d : 3;

    private int AutoSelectDegree(double[] x, double[] y, out double bestMetric)
    {
        int maxDegree = (int)DegreeSlider.Maximum;
        int bestDegree = 1;
        double bestAdjR2 = double.MinValue;

        for (int d = 1; d <= maxDegree; d++)
        {
            var reg = new PolynomialRegression(x, y, d);
            var coeffs = reg.Ols();
            var yPred = reg.Predict(x, coeffs);
            var metrics = reg.CalculateMetrics(y, yPred);
            double adjR2 = metrics.AdjustedR2;

            if (adjR2 > bestAdjR2)
            {
                bestAdjR2 = adjR2;
                bestDegree = d;
            }
            else
            {
                break;
            }
        }
        bestMetric = bestAdjR2;
        return bestDegree;
    }

    private void BuildModel_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_table is null) throw new InvalidOperationException("Сначала загрузите данные.");
            LoadArraysFromTable();
            if (_x is null || _y is null) throw new InvalidOperationException("Не удалось выделить X и Y.");

            int degree;
            if (AutoDegreeCheckBox.IsChecked == true)
            {
                BusyProgress.Visibility = Visibility.Visible;
                degree = AutoSelectDegree(_x, _y, out double bestMetric);
                BusyProgress.Visibility = Visibility.Collapsed;
                _vm.StatusText = $"Автоподбор: степень {degree}, AdjR²={bestMetric:F4}";
                DegreeSlider.Value = degree;
                DegreeTextBox.Text = degree.ToString();
            }
            else
            {
                degree = ParseDegree();
            }

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
            _vm.R2 = _metrics.Value.AdjustedR2;
            _vm.RegressionEquation = FormatEquation(_coeffs);
            _vm.StatusText = $"OK ({method}). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

            _vm.UpdateCoefficients(_coeffs);
            _vm.UpdatePredictions(_x, _yPred);

            Plot2D.Model = Plot2DModel(_x, _y, _yPred);
            ApplyGridVisibility();
        }
        catch (Exception ex)
        {
            BusyProgress.Visibility = Visibility.Collapsed;
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_x == null) return;

        var method = ((ComboBoxItem)MethodCombo.SelectedItem).Content?.ToString();
        if (method == "WLS")
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл весов (txt, csv, xlsx, db)",
                Filter = "Данные (*.txt;*.csv;*.xlsx;*.db;*.sqlite)|*.txt;*.csv;*.xlsx;*.db;*.sqlite|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    _weights = DatasetReader.LoadVector(dlg.FileName, _x.Length);
                    _vm.StatusText = $"Загружены веса из {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки весов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    _weights = null;
                }
            }
            else
            {
                _weights = null;
                _vm.StatusText = "Используются единичные веса (WLS)";
            }
        }
        else if (method == "GLS")
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите файл ковариационной матрицы (txt, csv, xlsx, db)",
                Filter = "Данные (*.txt;*.csv;*.xlsx;*.db;*.sqlite)|*.txt;*.csv;*.xlsx;*.db;*.sqlite|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    _cov = DatasetReader.LoadMatrix(dlg.FileName, _x.Length);
                    _vm.StatusText = $"Загружена ковариационная матрица из {dlg.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки матрицы: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    _cov = null;
                }
            }
            else
            {
                _cov = null;
                _vm.StatusText = "Используется единичная ковариационная матрица (GLS)";
            }
        }
        else
        {
            _weights = null;
            _cov = null;
            _vm.StatusText = "Метод OLS";
        }
    }

    private void BrowseAndLoad()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Данные (*.txt;*.csv;*.xlsx;*.db;*.sqlite)|*.txt;*.csv;*.xlsx;*.db;*.sqlite|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        SourcePathTextBox.Text = dlg.FileName;
        LoadFromPath(dlg.FileName);
    }

    private void ImportModel_Click(object sender, RoutedEventArgs e) => ImportModel();
    private void ExportModel_Click(object sender, RoutedEventArgs e) => ExportModel();

    private void ImportModel()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON модель (*.json)|*.json|Все файлы (*.*)|*.*",
            Title = "Выберите файл модели"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var model = ModelPersistence.LoadModel(dlg.FileName);

            _coeffs = model.Coefficients;
            _metrics = new RegressionMetrics(model.MSE, model.R2Adjusted);
            _yPred = model.Predictions;
            _sourcePath = model.SourceFile;

            if (model.X != null)
            {
                _x = model.X;
                _y = null;
            }
            else if (_x == null)
            {
                MessageBox.Show("Модель не содержит координат X. Сначала загрузите данные.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _vm.MSE = model.MSE;
            _vm.RMSE = Math.Sqrt(model.MSE);
            _vm.AdjustedR2 = model.R2Adjusted;
            _vm.R2 = model.R2Adjusted;
            _vm.RegressionEquation = FormatEquation(_coeffs);
            _vm.StatusText = $"Модель загружена из {dlg.FileName}";

            _vm.UpdateCoefficients(_coeffs);
            if (_x != null)
                _vm.UpdatePredictions(_x, _yPred);
            else
                _vm.ClearPredictions();

            if (_x != null && _y != null)
                Plot2D.Model = Plot2DModel(_x, _y, _yPred);
            else if (_x != null)
            {
                var model2D = CreateEmptyPlotModel();
                var line = new LineSeries { Title = "Модель", Color = OxyColor.Parse("#e74c3c"), StrokeThickness = 2 };
                for (int i = 0; i < _x.Length && i < _yPred.Length; i++)
                    line.Points.Add(new DataPoint(_x[i], _yPred[i]));
                model2D.Series.Add(line);
                Plot2D.Model = model2D;
            }
            ApplyGridVisibility();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки модели: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportModel()
    {
        if (_coeffs is null || _yPred is null || _metrics is null)
        {
            MessageBox.Show("Сначала постройте модель.", "Нет модели", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "JSON модель (*.json)|*.json|Все файлы (*.*)|*.*",
            AddExtension = true,
            FileName = "model"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            ModelPersistence.SaveModel(
                dlg.FileName,
                _coeffs,
                _metrics.Value.Mse,
                _metrics.Value.AdjustedR2,
                _yPred,
                _x,
                _sourcePath ?? "");
            _vm.StatusText = $"Модель экспортирована: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка экспорта модели: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PreviewGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _tableModified = true;
        _vm.StatusText = "Данные изменены. Сохраните изменения или загрузите заново.";
    }

    private void LoadFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Укажите путь к файлу/БД.");

        _sourcePath = path;
        BusyProgress.Visibility = Visibility.Visible;
        try
        {
            var loaded = _datasetReader.LoadAuto(path, HasHeadersCheckBox.IsChecked == true, previewRows: 200);
            _table = loaded.RawTable;
            _tableModified = false;

            PreviewGrid.ItemsSource = _table.DefaultView;
            _vm.Coefficients.Clear();
            _vm.Predictions.Clear();

            PopulateColumnCombos(_table);
            Populate3DColumnCombos();
            LoadArraysFromTable();

            _weights = null;
            _cov = null;

            if (_x is not null && _y is not null)
            {
                Plot2D.Model = Plot2DModel(_x, _y, yPred: null);
                ApplyGridVisibility();
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

        XColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(0);
        YColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(1);
    }

    private void Populate3DColumnCombos()
    {
        if (_table is null) return;
        var names = _table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        XColumn3DCombo.ItemsSource = names;
        YColumn3DCombo.ItemsSource = names;
        ZColumn3DCombo.ItemsSource = names;

        XColumn3DCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(0);
        YColumn3DCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(1);
        ZColumn3DCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("z", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(2);
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
        }
        else
        {
            var xy = DatasetReader.PrepareXY(_table);
            _x = xy.X;
            _y = xy.Y;
        }
    }

    private void LoadArraysFromTableFor3D()
    {
        if (_table is null) return;

        if (XColumn3DCombo.SelectedItem is string xName && YColumn3DCombo.SelectedItem is string yName && ZColumn3DCombo.SelectedItem is string zName
            && _table.Columns.Contains(xName) && _table.Columns.Contains(yName) && _table.Columns.Contains(zName))
        {
            var xs = new List<double>();
            var ys = new List<double>();
            var zs = new List<double>();
            foreach (DataRow row in _table.Rows)
            {
                if (!TryParseCell(row[xName], out var x)) continue;
                if (!TryParseCell(row[yName], out var y)) continue;
                if (!TryParseCell(row[zName], out var z)) continue;
                xs.Add(x);
                ys.Add(y);
                zs.Add(z);
            }
            _x3d = xs.ToArray();
            _y3d = ys.ToArray();
            _z3d = zs.ToArray();
        }
        else
        {
            var xyz = DatasetReader.PrepareXYZ(_table);
            _x3d = xyz.X;
            _y3d = xyz.Y;
            _z3d = xyz.Z;
            if (_table.Columns.Count >= 1) XColumn3DCombo.SelectedItem = _table.Columns[0].ColumnName;
            if (_table.Columns.Count >= 2) YColumn3DCombo.SelectedItem = _table.Columns[1].ColumnName;
            if (_table.Columns.Count >= 3) ZColumn3DCombo.SelectedItem = _table.Columns[2].ColumnName;
        }
    }

    private void Build3DModel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_table is null) throw new InvalidOperationException("Сначала загрузите данные.");
            LoadArraysFromTableFor3D();
            if (_x3d is null || _y3d is null || _z3d is null)
                throw new InvalidOperationException("Не удалось выделить X, Y и Z.");

            var selectedModel = ((ComboBoxItem)Model3DCombo.SelectedItem).Content?.ToString();
            double[] zPred = null;
            RegressionMetrics metrics;

            if (selectedModel == "Плоскость")
            {
                var regression = new PlaneRegression(_x3d, _y3d, _z3d);
                var result = regression.Fit();
                ViewModel3D.RegressionEquation = $"z = {result.A:F4}·x + {result.B:F4}·y + {result.C:F4}";
                metrics = result.Metrics;
                zPred = result.ZPred;
                ViewModel3D.UpdateCoefficients(new double[] { result.A, result.B, result.C });
            }
            else
            {
                var regression = new QuadraticSurfaceRegression(_x3d, _y3d, _z3d);
                var result = regression.Fit();
                ViewModel3D.RegressionEquation = "z = a·x² + b·y² + c·x·y + d·x + e·y + f";
                metrics = result.Metrics;
                zPred = result.ZPred;
                ViewModel3D.UpdateCoefficients(result.Coefficients);
            }

            ViewModel3D.MSE = metrics.Mse;
            ViewModel3D.RMSE = Math.Sqrt(metrics.Mse);
            ViewModel3D.AdjustedR2 = metrics.AdjustedR2;
            ViewModel3D.R2 = metrics.AdjustedR2;
            ViewModel3D.UpdatePredictions(_x3d, zPred);
            ViewModel3D.StatusText = "3D модель построена";

            Visualize3D(_x3d, _y3d, _z3d, zPred, selectedModel == "Плоскость");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка 3D", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clear3D_Click(object sender, RoutedEventArgs e)
    {
        Viewport3D.Children.Clear();
        ViewModel3D.RegressionEquation = string.Empty;
        ViewModel3D.StatusText = "3D сцена очищена";
        ViewModel3D.MSE = ViewModel3D.RMSE = ViewModel3D.R2 = ViewModel3D.AdjustedR2 = 0;
        ViewModel3D.Coefficients.Clear();
        ViewModel3D.Predictions.Clear();
    }

    private void Visualize3D(double[] x, double[] y, double[] z, double[] zPred, bool isPlane)
    {
        Viewport3D.Children.Clear();

        // Исходные точки
        var points = new PointsVisual3D();
        var pointCollection = new Point3DCollection(x.Length);
        for (int i = 0; i < x.Length; i++)
            pointCollection.Add(new Point3D(x[i], y[i], z[i]));
        points.Points = pointCollection;
        points.Color = Colors.RoyalBlue;
        points.Size = 3;
        Viewport3D.Children.Add(points);

        // Поверхность модели
        MeshGeometry3D mesh = BuildSurfaceMesh(x, y, zPred, isPlane);
        if (mesh != null)
        {
            var material = MaterialHelper.CreateMaterial(Colors.OrangeRed, 0.6);
            var model = new GeometryModel3D { Geometry = mesh, Material = material, BackMaterial = material };
            Viewport3D.Children.Add(new ModelVisual3D { Content = model });
        }

        // Оси координат
        // ... в методе Visualize3D
        // Создаем оси координат вручную
        var origin = new Point3D(0, 0, 0);
        // Ось X (красная)
        var axisX = new ArrowVisual3D
        {
            Point1 = origin,
            Point2 = new Point3D(5, 0, 0),
            Diameter = 0.1,
            Fill = Brushes.Red
        };
        Viewport3D.Children.Add(axisX);
        // Ось Y (зеленая)
        var axisY = new ArrowVisual3D
        {
            Point1 = origin,
            Point2 = new Point3D(0, 5, 0),
            Diameter = 0.1,
            Fill = Brushes.Green
        };
        Viewport3D.Children.Add(axisY);
        // Ось Z (синяя)
        var axisZ = new ArrowVisual3D
        {
            Point1 = origin,
            Point2 = new Point3D(0, 0, 5),
            Diameter = 0.1,
            Fill = Brushes.Blue
        };
        Viewport3D.Children.Add(axisZ);

        Viewport3D.ZoomExtents();
    }

    private MeshGeometry3D BuildSurfaceMesh(double[] x, double[] y, double[] zPred, bool isPlane)
    {
        if (x.Length == 0) return null;

        double minX = x.Min(), maxX = x.Max();
        double minY = y.Min(), maxY = y.Max();
        if (Math.Abs(maxX - minX) < 1e-6 || Math.Abs(maxY - minY) < 1e-6) return null;

        // Получаем коэффициенты модели из ViewModel3D
        var coeffs = ViewModel3D.Coefficients.Select(c => c.Value).ToArray();
        int p = coeffs.Length;
        if (p != 3 && p != 6) return null;

        const int resolution = 40;
        double stepX = (maxX - minX) / resolution;
        double stepY = (maxY - minY) / resolution;

        var positions = new List<Point3D>();
        var indices = new List<int>();

        // Генерация вершин
        for (int i = 0; i <= resolution; i++)
        {
            double xi = minX + i * stepX;
            for (int j = 0; j <= resolution; j++)
            {
                double yj = minY + j * stepY;
                double zi = 0;
                if (p == 3) // плоскость: a*x + b*y + c
                {
                    zi = coeffs[0] * xi + coeffs[1] * yj + coeffs[2];
                }
                else if (p == 6) // квадратичная
                {
                    zi = coeffs[0] * xi * xi + coeffs[1] * yj * yj + coeffs[2] * xi * yj +
                         coeffs[3] * xi + coeffs[4] * yj + coeffs[5];
                }
                positions.Add(new Point3D(xi, yj, zi));
            }
        }

        // Треугольники
        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                int idx = i * (resolution + 1) + j;
                int nextRow = (i + 1) * (resolution + 1) + j;
                indices.Add(idx);
                indices.Add(idx + 1);
                indices.Add(nextRow);

                indices.Add(nextRow);
                indices.Add(idx + 1);
                indices.Add(nextRow + 1);
            }
        }

        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            TriangleIndices = new Int32Collection(indices)
        };
        // Вычисление нормалей (метод расширения из HelixToolkit)
        ComputeNormals(mesh);
        return mesh;
    }

    private static bool TryParseCell(object? value, out double result)
    {
        var raw = value?.ToString() ?? string.Empty;
        raw = raw.Replace(',', '.').Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
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
        if (_coeffs is null || _yPred is null || _metrics is null)
        {
            MessageBox.Show(this, "Сначала постройте модель.", "Нет результата", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "JSON модель (*.json)|*.json|TXT (*.txt)|*.txt|CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx|SQLite (*.db;*.sqlite)|*.db;*.sqlite|Все файлы (*.*)|*.*",
            AddExtension = true,
            FileName = "model"
        };
        if (dlg.ShowDialog(this) != true) return;

        if (Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".json")
        {
            ModelPersistence.SaveModel(
                dlg.FileName,
                _coeffs,
                _metrics.Value.Mse,
                _metrics.Value.AdjustedR2,
                _yPred,
                _x,
                _sourcePath ?? "");
        }
        else
        {
            _resultSaver.SaveResults(dlg.FileName, _coeffs, _metrics.Value.Mse, _metrics.Value.AdjustedR2, _yPred, _sourcePath ?? "");
        }

        _vm.StatusText = $"Сохранено: {dlg.FileName}";
    }

    private static string FormatEquation(IReadOnlyList<double> coeffs)
    {
        if (coeffs.Count == 0) return "";
        char[] superscripts = { '⁰', '¹', '²', '³', '⁴', '⁵', '⁶', '⁷', '⁸', '⁹' };
        var parts = new List<string>();
        for (int i = 0; i < coeffs.Count; i++)
        {
            double a = coeffs[i];
            if (Math.Abs(a) < 1e-15) continue;
            string signPart;
            if (parts.Count == 0)
                signPart = a.ToString("G6", CultureInfo.InvariantCulture);
            else
                signPart = (a >= 0 ? "+ " : "- ") + Math.Abs(a).ToString("G6", CultureInfo.InvariantCulture);
            string varPart = i switch
            {
                0 => "",
                1 => "·x",
                _ => "·x" + string.Concat(i.ToString().Select(c => superscripts[c - '0']))
            };
            parts.Add(signPart + varPart);
        }
        return parts.Count == 0 ? "y = 0" : "y = " + string.Join(" ", parts);
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
            MarkerFill = OxyColor.Parse("#007acc")
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

    private void ApplyGridVisibility()
    {
        if (Plot2D.Model == null) return;
        var style = ShowGridCheckBox.IsChecked == true ? LineStyle.Solid : LineStyle.None;
        foreach (var axis in Plot2D.Model.Axes)
            axis.MajorGridlineStyle = style;
        Plot2D.InvalidatePlot(true);
    }

    private void ShowGridCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyGridVisibility();

    private void ExportPlot_Click(object sender, RoutedEventArgs e)
    {
        if (Plot2D.Model == null)
        {
            MessageBox.Show("Нет графика для экспорта.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|SVG Image (*.svg)|*.svg",
            AddExtension = true,
            FileName = "plot"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            if (dlg.FilterIndex == 1)
            {
                var exporter = new PngExporter { Width = 800, Height = 600, Resolution = 96 };
                using var stream = File.Create(dlg.FileName);
                exporter.Export(Plot2D.Model, stream);
            }
            else
            {
                var exporter = new OxyPlot.Wpf.SvgExporter { Width = 800, Height = 600 };
                using var stream = File.Create(dlg.FileName);
                exporter.Export(Plot2D.Model, stream);
            }
            _vm.StatusText = $"График сохранён: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка экспорта графика: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_coeffs is null || _metrics is null || _yPred is null)
        {
            MessageBox.Show("Сначала постройте модель.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Excel отчёт (*.xlsx)|*.xlsx|Word отчёт (*.docx)|*.docx|Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
            AddExtension = true,
            FileName = "report"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            if (dlg.FilterIndex == 1)
            {
                _resultSaver.SaveReportToExcel(
                    dlg.FileName,
                    _coeffs,
                    _metrics.Value.Mse,
                    _metrics.Value.AdjustedR2,
                    _yPred,
                    _x,
                    _sourcePath ?? "",
                    _vm.RegressionEquation);
            }
            else if (dlg.FilterIndex == 2)
            {
                _resultSaver.SaveReportToWord(
                    dlg.FileName,
                    _coeffs,
                    _metrics.Value.Mse,
                    _metrics.Value.AdjustedR2,
                    _yPred,
                    _x,
                    _sourcePath ?? "",
                    _vm.RegressionEquation);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("ОТЧЁТ ПО РЕГРЕССИОННОЙ МОДЕЛИ");
                sb.AppendLine(new string('=', 40));
                sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Источник данных: {_sourcePath ?? "не указан"}");
                sb.AppendLine();
                sb.AppendLine("УРАВНЕНИЕ РЕГРЕССИИ:");
                sb.AppendLine(_vm.RegressionEquation);
                sb.AppendLine();
                sb.AppendLine("КОЭФФИЦИЕНТЫ:");
                for (int i = 0; i < _coeffs.Length; i++)
                    sb.AppendLine($"  a{i} = {_coeffs[i]:G6}");
                sb.AppendLine();
                sb.AppendLine("МЕТРИКИ КАЧЕСТВА:");
                sb.AppendLine($"  MSE            = {_vm.MSE:F6}");
                sb.AppendLine($"  RMSE           = {_vm.RMSE:F6}");
                sb.AppendLine($"  R²             = {_vm.R2:F6}");
                sb.AppendLine($"  Скоррект. R²   = {_vm.AdjustedR2:F6}");

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            }

            _vm.StatusText = $"Отчёт сохранён: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения отчёта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        } 
    }

    private void ComputeNormals(MeshGeometry3D mesh)
    {
        var positions = mesh.Positions;
        var indices = mesh.TriangleIndices;
        var normals = new Vector3D[positions.Count];
        for (int i = 0; i < indices.Count; i += 3)
        {
            var v0 = positions[indices[i]];
            var v1 = positions[indices[i + 1]];
            var v2 = positions[indices[i + 2]];
            var normal = Vector3D.CrossProduct(v1 - v0, v2 - v0);
            normal.Normalize();
            normals[indices[i]] += normal;
            normals[indices[i + 1]] += normal;
            normals[indices[i + 2]] += normal;
        }
        var normalCollection = new Vector3DCollection();
        foreach (var n in normals)
        {
            var normalizedNormal = n;
            normalizedNormal.Normalize();
            normalCollection.Add(normalizedNormal);
        }
        mesh.Normals = normalCollection;
    }
}
