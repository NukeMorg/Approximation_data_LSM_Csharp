using CurseWork.Core.FileIO;
using CurseWork.Core.Regression;
using CurseWork.Core.Regression.Regression2D;
using CurseWork.Core.Report;
using CurseWork.ViewModels;
using CurseWork.Views;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CurseWork
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly DatasetReader _datasetReader = new();
        private readonly TableSourceSaver _tableSourceSaver = new();
        private readonly ReportService _reportService = new();

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

            _vm.ThreeD.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Main3DViewModel.SurfaceColor))
                {
                    Properties.Settings.Default.SurfaceColor = _vm.ThreeD.SurfaceColor.ToString();
                    Properties.Settings.Default.Save();
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

            // Загрузка сохранённых цветов
            try
            {
                var savedLineColor = (Color)ColorConverter.ConvertFromString(Properties.Settings.Default.LineColor);
                _vm.LineColor = savedLineColor;

                if (_vm.ThreeD != null)
                {
                    var savedSurfaceColor = (Color)ColorConverter.ConvertFromString(Properties.Settings.Default.SurfaceColor);
                    _vm.ThreeD.SurfaceColor = savedSurfaceColor;
                }
            }
            catch
            {
                // Если значение повреждено, оставляем цвет по умолчанию
            }
        }

        private void MenuClose_OnClick(object sender, RoutedEventArgs e)
        {
            // Сброс данных и интерфейса – аналог «Закрыть файл»
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

            _vm.RegressionEquation = "";
            _vm.Coefficients.Clear();
            _vm.Predictions.Clear();
            _vm.MSE = _vm.RMSE = _vm.R2 = _vm.AdjustedR2 = 0;
            _vm.StatusText = "Готово";

            Plot2D.Model = CreateEmptyPlotModel();
            ApplyGridVisibility();
            Viewport3D.Children.Clear();
            Viewport3D.Children.Add(new DefaultLights());
        }

        private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HelpWindow();
            helpWindow.Owner = this;
            helpWindow.ShowDialog();
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = this;
            aboutWindow.ShowDialog();
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.LineColor) && _modelLineSeries != null)
            {
                var newColor = _vm.LineColor;
                _modelLineSeries.Color = OxyColor.FromRgb(newColor.R, newColor.G, newColor.B);
                Plot2D.InvalidatePlot(true);   // перерисовать без полной очистки

                Properties.Settings.Default.LineColor = _vm.LineColor.ToString();
                Properties.Settings.Default.Save();
            }
        }

        private byte[]? CaptureViewport3D()
        {
            try
            {
                // Рендерим Viewport3D в растровое изображение
                var renderBitmap = new RenderTargetBitmap(
                    (int)Viewport3D.ActualWidth, (int)Viewport3D.ActualHeight,
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(Viewport3D);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                // Логирование ошибки (по желанию)
                System.Diagnostics.Debug.WriteLine($"Ошибка скриншота 3D: {ex.Message}");
                return null;
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
        private async Task Build2DModelAsync()
        {
            LoadArraysFromTable();
            if (_x is null || _y is null) throw new InvalidOperationException("Не удалось выделить X и Y.");

            int degree;
            if (AutoDegreeCheckBox.IsChecked == true)
            {
                _vm.IsBusy = true; // показываем индикатор
                int maxDegree = (int)DegreeSlider.Maximum; // читаем в UI-потоке

                var result = await Task.Run(() =>
                {
                    int bestD = AutoSelectDegree(_x, _y, maxDegree, out double bestMetric);
                    return (bestD, bestMetric);
                });
                degree = result.bestD;
                _vm.StatusText = $"Автоподбор: степень {degree}, AdjR²={result.bestMetric:F4}";
                DegreeSlider.Value = degree;
                DegreeTextBox.Text = degree.ToString();
            }
            else degree = ParseDegree();

            var method = ((ComboBoxItem)MethodCombo.SelectedItem).Content?.ToString() ?? "OLS";
            var reg = new PolynomialRegression(_x, _y, degree);

            // Тяжёлые вычисления – в фоне
            _vm.IsBusy = true;
            try
            {
                var coeffs = await Task.Run(() => method switch
                {
                    "WLS" => reg.Wls(_weights),
                    "GLS" => reg.Gls(_cov),
                    _ => reg.Ols()
                });
                var yPred = await Task.Run(() => reg.Predict(_x, coeffs));
                var metrics = await Task.Run(() => reg.CalculateMetrics(_y, yPred));

                // Обновление UI
                _coeffs = coeffs;
                _yPred = yPred;
                _metrics = metrics;

                _vm.MSE = metrics.Mse;
                _vm.RMSE = Math.Sqrt(_vm.MSE);
                _vm.AdjustedR2 = metrics.AdjustedR2;
                _vm.R2 = metrics.AdjustedR2;
                _vm.RegressionEquation = FormatEquation(_coeffs);
                _vm.StatusText = $"OK ({method}). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

                _vm.UpdateCoefficients(_coeffs);
                _vm.UpdatePredictions(_x, _yPred);
                Plot2D.Model = Plot2DModel(_x, _y, _yPred);
                ApplyGridVisibility();
            }
            finally
            {
                _vm.IsBusy = false;
            }
        }

        private async void Build2DModel_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await Build2DModelAsync();
            }
            catch (Exception ex)
            {
                _vm.IsBusy = false;
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) { }

        private void MenuOpen_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();

        private void MenuExit_OnClick(object sender, RoutedEventArgs e) => Close();
        private void Plot2D_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Plot2D.ResetAllAxes();

        private void BrowseSource_OnClick(object sender, RoutedEventArgs e) => BrowseAndLoad();

        private async void LoadData_OnClick(object sender, RoutedEventArgs e)
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
                    await LoadFromPathAsync(SourcePathTextBox.Text);
            }
            catch (Exception ex)
            {
                _vm.IsBusy = false;
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
            ShowGridCheckBox.Visibility = is3D ? Visibility.Collapsed : Visibility.Visible;

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
            {
                Plot2D.Model = CreateEmptyPlotModel();
                ApplyGridVisibility();
            }
            else
            {
                Viewport3D.Children.Clear();
                Viewport3D.Children.Add(new DefaultLights());
            }

            _modelLineSeries = null;
        }

        //private void Build2DModel()
        //{
        //    LoadArraysFromTable();
        //    if (_x is null || _y is null) throw new InvalidOperationException("Не удалось выделить X и Y.");

        //    int degree;
        //    if (AutoDegreeCheckBox.IsChecked == true)
        //    {
        //        BusyProgress.Visibility = Visibility.Visible;
        //        degree = AutoSelectDegree(_x, _y, out double bestMetric);
        //        BusyProgress.Visibility = Visibility.Collapsed;
        //        _vm.StatusText = $"Автоподбор: степень {degree}, AdjR²={bestMetric:F4}";
        //        DegreeSlider.Value = degree;
        //        DegreeTextBox.Text = degree.ToString();
        //    }
        //    else degree = ParseDegree();

        //    var method = ((ComboBoxItem)MethodCombo.SelectedItem).Content?.ToString() ?? "OLS";
        //    var reg = new PolynomialRegression(_x, _y, degree);
        //    _coeffs = method switch
        //    {
        //        "WLS" => reg.Wls(_weights),
        //        "GLS" => reg.Gls(_cov),
        //        _ => reg.Ols()
        //    };

        //    _yPred = reg.Predict(_x, _coeffs);
        //    _metrics = reg.CalculateMetrics(_y, _yPred);

        //    _vm.MSE = _metrics.Value.Mse;
        //    _vm.RMSE = Math.Sqrt(_vm.MSE);
        //    _vm.AdjustedR2 = _metrics.Value.AdjustedR2;
        //    _vm.R2 = _metrics.Value.AdjustedR2;
        //    _vm.RegressionEquation = FormatEquation(_coeffs);
        //    _vm.StatusText = $"OK ({method}). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

        //    _vm.UpdateCoefficients(_coeffs);
        //    _vm.UpdatePredictions(_x, _yPred);
        //    Plot2D.Model = Plot2DModel(_x, _y, _yPred);
        //    ApplyGridVisibility();
        //}


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

        private int AutoSelectDegree(double[] x, double[] y, int maxDegree, out double bestMetric)
        {
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

        private async void BrowseAndLoad()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Данные (*.txt;*.csv;*.xlsx;*.db;*.sqlite)|*.txt;*.csv;*.xlsx;*.db;*.sqlite|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            SourcePathTextBox.Text = dlg.FileName;
            await LoadFromPathAsync(dlg.FileName);
        }

        private async Task LoadFromPathAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Укажите путь к файлу/БД.");

            _sourcePath = path;
            _vm.IsBusy = true;
            try
            {
                bool hasHeaders = HasHeadersCheckBox.IsChecked == true; // ← прочитали в UI-потоке

                // Загрузка файла – потенциально длительная операция
                var loaded = await Task.Run(() => _datasetReader.LoadAuto(path, hasHeaders, previewRows: 200));
                _table = loaded.RawTable;
                _tableModified = false;

                // Обновление UI – эти методы быстро работают, можно вызывать прямо в UI-потоке
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
                _vm.IsBusy = false;
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

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            IRegressionResult? result = null;

            if (Toggle2D.IsChecked == true)
            {
                if (_coeffs == null || _metrics == null || _x == null || _yPred == null)
                {
                    MessageBox.Show("Сначала постройте 2D модель.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Regression2DResult(
                    _vm.RegressionEquation,
                    _coeffs,
                    _metrics.Value,
                    _x,
                    _yPred,
                    Plot2D.Model);
            }
            else if (Toggle3D.IsChecked == true)
            {
                var vm3d = _vm.ThreeD;
                if (vm3d == null || vm3d.Coefficients.Count == 0)
                {
                    MessageBox.Show("Сначала постройте 3D модель.", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    var (xArr, yArr, zArr, zPred) = vm3d.GetVisualizationData();
                    result = new Regression3DResult(
                        vm3d.RegressionEquation,
                        vm3d.Coefficients.Select(c => c.Value).ToArray(),
                        new RegressionMetrics(vm3d.MSE, vm3d.AdjustedR2),
                        xArr,
                        yArr,
                        zPred,
                        CaptureViewport3D());
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Word отчёт (*.docx)|*.docx|Excel отчёт (*.xlsx)|*.xlsx|Текстовый (*.txt)|*.txt|CSV (*.csv)|*.csv|SQLite БД (*.db)|*.db|Все файлы|*.*",
                AddExtension = true,
                FileName = "report"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                _reportService.SaveReport(dlg.FileName, result);
                _vm.StatusText = $"Отчёт сохранён: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                MajorGridlineStyle = LineStyle.None          // ← было LineStyle.Dash
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                MajorGridlineStyle = LineStyle.None
            });
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
            // Определяем активный режим
            bool is2D = Toggle2D.IsChecked == true;
            bool is3D = Toggle3D.IsChecked == true;

            if (is2D && Plot2D.Model == null)
            {
                MessageBox.Show("Нет 2D графика для экспорта.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (is3D && Viewport3D.Children.Count == 0)
            {
                MessageBox.Show("Нет 3D сцены для экспорта.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = is2D
                    ? "PNG Image (*.png)|*.png|SVG Image (*.svg)|*.svg"
                    : "PNG Image (*.png)|*.png",   // для 3D только PNG
                AddExtension = true,
                FileName = is2D ? "plot2D" : "plot3D"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                if (is2D)
                {
                    if (dlg.FilterIndex == 1) // PNG
                    {
                        var exporter = new PngExporter { Width = 800, Height = 600, Resolution = 96 };
                        using var stream = File.Create(dlg.FileName);
                        exporter.Export(Plot2D.Model, stream);
                    }
                    else // SVG
                    {
                        var exporter = new OxyPlot.Wpf.SvgExporter { Width = 800, Height = 600 };
                        using var stream = File.Create(dlg.FileName);
                        exporter.Export(Plot2D.Model, stream);
                    }
                }
                else // 3D
                {
                    var imageBytes = CaptureViewport3D();
                    if (imageBytes != null)
                    {
                        File.WriteAllBytes(dlg.FileName, imageBytes);
                    }
                    else
                    {
                        MessageBox.Show("Не удалось захватить 3D изображение.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                _vm.StatusText = $"График сохранён: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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