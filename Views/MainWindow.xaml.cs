using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CurseWork.Core.FileIO;
using CurseWork.Core.Regression;
using CurseWork.Core.Regression.Regression2D;
using CurseWork.Core.Saving;
using CurseWork.ViewModels;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace CurseWork
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly DatasetReader _datasetReader = new();
        private readonly ResultSaver _resultSaver = new();
        private readonly TableSourceSaver _tableSourceSaver = new();

        private DataTable? _table;
        private string? _sourcePath;
        private bool _tableModified;

        // 2D данные
        private double[]? _x, _y;
        private double[]? _weights;
        private double[,]? _cov;
        private double[]? _coeffs;
        private double[]? _yPred;
        private RegressionMetrics? _metrics;
        private LineSeries? _modelLineSeries;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel { StatusText = "Готово" };
            _vm.PropertyChanged += OnViewModelPropertyChanged;

            _vm.ThreeD = new Main3DViewModel();
            DataContext = _vm;

            // Подписка на перерисовку 3D-сцены
            _vm.ThreeD.RequestVisualize += () =>
            {
                try
                {
                    var data = _vm.ThreeD.GetVisualizationData();
                    // Извлекаем коэффициенты из 3D ViewModel
                    double[] coeffs = _vm.ThreeD.Coefficients.Select(c => c.Value).ToArray();
                    Visualize3D(data.x, data.y, data.z, data.zPred, _vm.ThreeD.SurfaceColor, coeffs);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка визуализации", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Проброс метрик и уравнения в MainViewModel
            _vm.ThreeD.PropertyChanged += (s, e) =>
            {
                if (_vm.ThreeD == null) return;
                switch (e.PropertyName)
                {
                    case nameof(Main3DViewModel.RegressionEquation):
                        _vm.RegressionEquation = _vm.ThreeD.RegressionEquation;
                        break;
                    case nameof(Main3DViewModel.MSE):
                        _vm.MSE = _vm.ThreeD.MSE;
                        break;
                    case nameof(Main3DViewModel.RMSE):
                        _vm.RMSE = _vm.ThreeD.RMSE;
                        break;
                    case nameof(Main3DViewModel.R2):
                        _vm.R2 = _vm.ThreeD.R2;
                        break;
                    case nameof(Main3DViewModel.AdjustedR2):
                        _vm.AdjustedR2 = _vm.ThreeD.AdjustedR2;
                        break;
                }
            };

            // Проброс коллекций
            _vm.ThreeD.ModelBuilt += () =>
            {
                if (_vm.ThreeD == null) return;
                // Коэффициенты
                _vm.Coefficients.Clear();
                foreach (var c in _vm.ThreeD.Coefficients)
                    _vm.Coefficients.Add(new CoefficientItem { Index = c.Index, Value = c.Value });
                // Предсказания
                _vm.Predictions.Clear();
                foreach (var p in _vm.ThreeD.Predictions)
                    _vm.Predictions.Add(new PredictionItem { X = p.X, PredictedY = p.PredictedZ });
            };

            Plot2D.Model = CreateEmptyPlotModel();
            ApplyGridVisibility();

            DegreeSlider.ValueChanged += DegreeSlider_ValueChanged;
            DegreeTextBox.TextChanged += DegreeTextBox_TextChanged;
            DegreeSlider.Value = 3;
            DegreeTextBox.Text = "3";

            // Настройка 3D камеры
            Viewport3D.Camera = new PerspectiveCamera
            {
                Position = new Point3D(10, 10, 10),
                LookDirection = new Vector3D(-10, -10, -10),
                UpDirection = new Vector3D(0, 0, 1)
            };

            Toggle2D.SetValue(RadioButton.GroupNameProperty, "dim");
            Toggle3D.SetValue(RadioButton.GroupNameProperty, "dim");
            Toggle2D.IsChecked = true;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LineColor) && _modelLineSeries != null)
            {
                var newColor = _vm.LineColor;
                _modelLineSeries.Color = OxyColor.FromRgb(newColor.R, newColor.G, newColor.B);
                Plot2D.InvalidatePlot(true);   // перерисовать без полной очистки
            }
        }

        private void Visualize3D(double[] x, double[] y, double[] z, double[] zPred, Color surfaceColor, double[] coeffs)
        {
            Viewport3D.Children.Clear();
            Viewport3D.Children.Add(new DefaultLights());

            // Исходные точки (тёмно-синие)
            var points = new PointsVisual3D();
            var pointCollection = new Point3DCollection(x.Length);
            for (int i = 0; i < x.Length; i++)
                pointCollection.Add(new Point3D(x[i], y[i], z[i]));
            points.Points = pointCollection;
            points.Color = Colors.RoyalBlue;
            points.Size = 4;
            Viewport3D.Children.Add(points);

            // Поверхность модели
            MeshGeometry3D mesh = BuildSurfaceMesh(x, y, zPred, coeffs);
            if (mesh != null)
            {
                var brush = new SolidColorBrush(surfaceColor)
                {
                    Opacity = 0.5
                };
                brush.Freeze();

                var material = new DiffuseMaterial(brush);

                var model = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = material,
                    BackMaterial = material
                };

                Viewport3D.Children.Add(new ModelVisual3D { Content = model });
            }

            // ------------- ОСИ КООРДИНАТ -------------
            var origin = new Point3D(0, 0, 0);
            double length = 8.0;
            double arrowDiameter = 0.12;

            var arrowX = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(length, 0, 0),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Red)
            };
            Viewport3D.Children.Add(arrowX);

            var arrowY = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(0, length, 0),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Green)
            };
            Viewport3D.Children.Add(arrowY);

            var arrowZ = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(0, 0, length),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Blue)
            };
            Viewport3D.Children.Add(arrowZ);

            // Подписи осей
            AddAxisLabel(Viewport3D, "X", new Point3D(length + 0.4, 0, 0), Colors.Red);
            AddAxisLabel(Viewport3D, "Y", new Point3D(0, length + 0.4, 0), Colors.Green);
            AddAxisLabel(Viewport3D, "Z", new Point3D(0, 0, length + 0.4), Colors.Blue);

            Viewport3D.ZoomExtents();
            Viewport3D.InvalidateVisual();
        }

        // Кнопка "Построить 2D модель"
        private void Build2DModel_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Build2DModel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            try
            {
                if (string.IsNullOrWhiteSpace(SourcePathTextBox.Text))
                {
                    BrowseAndLoad();
                    return;
                }

                if (_tableModified) OfferSaveEdits();

                if (_table != null)
                    ApplyCurrentTableToData();
                else
                    LoadFromPath(SourcePathTextBox.Text);
            }
            catch (Exception ex)
            {
                BusyProgress.Visibility = Visibility.Collapsed;
                MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyCurrentTableToData()
        {
            if (_table == null) return;

            if (Toggle2D.IsChecked == true)
            {
                LoadArraysFromTable();
                if (_x != null && _y != null)
                {
                    Plot2D.Model = Plot2DModel(_x, _y, yPred: null);
                    ApplyGridVisibility();
                    _vm.StatusText = $"Применены новые столбцы. Точек: {_x.Length}";
                }
            }
            else
            {
                var xyz = LoadArraysFromTableFor3D();
                if (xyz != null)
                {
                    var (xArr, yArr, zArr) = xyz.Value;
                    _vm.ThreeD?.SetData(xArr, yArr, zArr);
                    Show3DPointsOnly(xArr, yArr, zArr);
                    _vm.StatusText = $"Применены новые столбцы. Точек: {xArr.Length}";
                }
                UpdatePreviewGridColumns();
            }

            _coeffs = null;
            _yPred = null;
            _metrics = null;
            _vm.RegressionEquation = "";
            _vm.Coefficients.Clear();
            _vm.Predictions.Clear();
            _vm.MSE = _vm.RMSE = _vm.R2 = _vm.AdjustedR2 = 0;
        }

        private void UpdatePreviewGridColumns()
        {
            if (_table == null) { PreviewGrid.ItemsSource = null; return; }

            var selectedColumns = new List<string>();
            if (Toggle2D.IsChecked == true)
            {
                if (XColumnCombo.SelectedItem is string xName) selectedColumns.Add(xName);
                if (YColumnCombo.SelectedItem is string yName) selectedColumns.Add(yName);
            }
            else
            {
                if (XColumnCombo.SelectedItem is string xName) selectedColumns.Add(xName);
                if (YColumnCombo.SelectedItem is string yName) selectedColumns.Add(yName);
                if (ZColumnCombo.SelectedItem is string zName) selectedColumns.Add(zName);
            }

            if (selectedColumns.Count == 0) { PreviewGrid.ItemsSource = _table.DefaultView; return; }

            var filteredTable = new DataTable();
            foreach (var colName in selectedColumns)
                if (_table.Columns.Contains(colName))
                    filteredTable.Columns.Add(colName, typeof(string));

            foreach (DataRow row in _table.Rows)
            {
                var newRow = filteredTable.NewRow();
                foreach (var colName in selectedColumns)
                    if (_table.Columns.Contains(colName))
                        newRow[colName] = row[colName]?.ToString();
                filteredTable.Rows.Add(newRow);
            }

            PreviewGrid.ItemsSource = filteredTable.DefaultView;
        }

        private void ToggleMode_Checked(object sender, RoutedEventArgs e)
        {
            if (Toggle3D == null || Toggle2D == null) return;

            if (sender == Toggle2D && Toggle2D.IsChecked == true)
                Toggle3D.IsChecked = false;
            else if (sender == Toggle3D && Toggle3D.IsChecked == true)
                Toggle2D.IsChecked = false;

            bool is3D = Toggle3D.IsChecked == true;

            _table = null;
            _sourcePath = null;
            _tableModified = false;
            _x = _y = null;
            _weights = null; _cov = null;
            _coeffs = null; _yPred = null; _metrics = null;

            PreviewGrid.ItemsSource = null;
            XColumnCombo.ItemsSource = null;
            YColumnCombo.ItemsSource = null;
            ZColumnCombo.ItemsSource = null;
            SourcePathTextBox.Text = "";

            ZLabel.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
            ZColumnCombo.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
            Panel2DSettings.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
            Panel3DSettings.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;
            Plot2DBorder.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;
            Plot3DBorder.Visibility = is3D ? Visibility.Visible : Visibility.Collapsed;

            _vm.RegressionEquation = "";
            _vm.Coefficients.Clear();
            _vm.Predictions.Clear();
            _vm.MSE = _vm.RMSE = _vm.R2 = _vm.AdjustedR2 = 0;

            if (!is3D)
                Plot2D.Model = CreateEmptyPlotModel();
            else
            {
                Viewport3D.Children.Clear();
                Viewport3D.Children.Add(new DefaultLights());
            }

            _modelLineSeries = null;
        }

        private void Build2DModel()
        {
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
            else degree = ParseDegree();

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


        private void AddAxisLabel(HelixViewport3D viewport, string text, Point3D position, Color color)
        { /* ... код без изменений ... */
            viewport.Children.Add(new BillboardTextVisual3D
            {
                Text = text,
                Position = position,
                Foreground = new SolidColorBrush(color),
                FontSize = 14,
                FontWeight = System.Windows.FontWeights.Bold,
                Background = Brushes.Transparent,
                Padding = new Thickness(2)
            });
        }

        private MeshGeometry3D BuildSurfaceMesh(double[] x, double[] y, double[] zPred, double[] coeffs)
        {
            if (x.Length == 0) return null;
            double minX = x.Min(), maxX = x.Max();
            double minY = y.Min(), maxY = y.Max();
            if (Math.Abs(maxX - minX) < 1e-6 || Math.Abs(maxY - minY) < 1e-6) return null;

            int resolution = 40;
            double stepX = (maxX - minX) / resolution;
            double stepY = (maxY - minY) / resolution;

            int p = coeffs.Length;                     // теперь это локальный массив

            var positions = new List<Point3D>();
            var indices = new List<int>();

            for (int i = 0; i <= resolution; i++)
            {
                double xi = minX + i * stepX;
                for (int j = 0; j <= resolution; j++)
                {
                    double yj = minY + j * stepY;
                    double zi = 0;
                    if (p == 3)
                        zi = coeffs[0] * xi + coeffs[1] * yj + coeffs[2];
                    else if (p == 6)
                        zi = coeffs[0] * xi * xi + coeffs[1] * yj * yj + coeffs[2] * xi * yj +
                             coeffs[3] * xi + coeffs[4] * yj + coeffs[5];
                    else
                        return null;                    // на всякий случай
                    positions.Add(new Point3D(xi, yj, zi));
                }
            }

            for (int i = 0; i < resolution; i++)
                for (int j = 0; j < resolution; j++)
                {
                    int idx = i * (resolution + 1) + j;
                    int nextRow = (i + 1) * (resolution + 1) + j;
                    indices.Add(idx); indices.Add(idx + 1); indices.Add(nextRow);
                    indices.Add(nextRow); indices.Add(idx + 1); indices.Add(nextRow + 1);
                }

            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(positions),
                TriangleIndices = new Int32Collection(indices)
            };
            ComputeNormals(mesh);
            return mesh;
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
                var nn = n;
                if (nn.LengthSquared > 0) nn.Normalize();
                normalCollection.Add(nn);
            }
            mesh.Normals = normalCollection;
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

                UpdatePreviewGridColumns();
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

                if (Toggle3D.IsChecked == true)
                {
                    var xyz = LoadArraysFromTableFor3D();
                    if (xyz != null)
                    {
                        var (xArr, yArr, zArr) = xyz.Value;
                        _vm.ThreeD?.SetData(xArr, yArr, zArr);
                        Show3DPointsOnly(xArr, yArr, zArr);
                    }
                }
            }
            finally
            {
                BusyProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void Show3DPointsOnly(double[] x, double[] y, double[] z)
        {
            Viewport3D.Children.Clear();
            Viewport3D.Children.Add(new DefaultLights());

            var points = new PointsVisual3D();
            var pointCollection = new Point3DCollection(x.Length);
            for (int i = 0; i < x.Length; i++)
                pointCollection.Add(new Point3D(x[i], y[i], z[i]));
            points.Points = pointCollection;
            points.Color = Colors.RoyalBlue;
            points.Size = 4;
            Viewport3D.Children.Add(points);

            var origin = new Point3D(0, 0, 0);
            double length = 8.0;
            double arrowDiameter = 0.12;

            var arrowX = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(length, 0, 0),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Red)
            };
            Viewport3D.Children.Add(arrowX);

            var arrowY = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(0, length, 0),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Green)
            };
            Viewport3D.Children.Add(arrowY);

            var arrowZ = new ArrowVisual3D
            {
                Point1 = origin,
                Point2 = new Point3D(0, 0, length),
                Diameter = arrowDiameter,
                Fill = new SolidColorBrush(Colors.Blue)
            };
            Viewport3D.Children.Add(arrowZ);

            AddAxisLabel(Viewport3D, "X", new Point3D(length + 0.4, 0, 0), Colors.Red);
            AddAxisLabel(Viewport3D, "Y", new Point3D(0, length + 0.4, 0), Colors.Green);
            AddAxisLabel(Viewport3D, "Z", new Point3D(0, 0, length + 0.4), Colors.Blue);

            Viewport3D.ZoomExtents();
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
            ZColumnCombo.ItemsSource = names;
            if (names.Count > 0)
                ZColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("z", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(2);
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

        // Возвращает nullable-кортеж с тремя массивами
        private (double[] X, double[] Y, double[] Z)? LoadArraysFromTableFor3D()
        {
            if (_table is null) return null;

            string xName = XColumnCombo.SelectedItem as string ?? _table.Columns[0].ColumnName;
            string yName = YColumnCombo.SelectedItem as string ?? _table.Columns[1].ColumnName;
            string zName = ZColumnCombo.SelectedItem as string ?? (_table.Columns.Count > 2 ? _table.Columns[2].ColumnName : _table.Columns[0].ColumnName);

            if (_table.Columns.Contains(xName) && _table.Columns.Contains(yName) && _table.Columns.Contains(zName))
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
                return (xs.ToArray(), ys.ToArray(), zs.ToArray());
            }
            else
            {
                var xyz = DatasetReader.PrepareXYZ(_table);
                return (xyz.X, xyz.Y, xyz.Z);
            }
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

        private void PreviewGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            _tableModified = true;
            _vm.StatusText = "Данные изменены. Сохраните изменения или загрузите заново.";
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

        private PlotModel Plot2DModel(double[] x, double[] y, double[]? yPred)
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
                var lineColor = _vm.LineColor;
                var oxyColor = OxyColor.FromRgb(lineColor.R, lineColor.G, lineColor.B);
                var line = new LineSeries { Title = "Модель", Color = oxyColor, StrokeThickness = 2 };
                for (var i = 0; i < x.Length; i++)
                    line.Points.Add(new DataPoint(x[i], yPred[i]));
                model.Series.Add(line);

                _modelLineSeries = line;
            }
            else
                _modelLineSeries = null;

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

        private static bool TryParseCell(object? value, out double result)
        {
            var raw = value?.ToString() ?? string.Empty;
            raw = raw.Replace(',', '.').Trim();
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}