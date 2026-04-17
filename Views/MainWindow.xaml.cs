using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using CurseWork.Core.FileIO;
using CurseWork.Core.Regression;
using CurseWork.Core.Saving;

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

    private double[]? _weights = null;
    private double[,]? _cov = null;

    private double[]? _coeffs;
    private double[]? _yPred;
    private RegressionMetrics? _metrics;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel { StatusText = "Готово" };
        DataContext = _vm;
        Plot2D.Model = CreateEmptyPlotModel();
        ApplyGridVisibility();

        // Синхронизация слайдера и текстового поля степени
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

    private int ParseDegree()
    {
        if (int.TryParse(DegreeTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 1)
            return d;
        return 3;
    }

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
                // При первом же ухудшении прекращаем перебор
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
            _vm.R2 = _vm.AdjustedR2;
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
            {
                Plot2D.Model = Plot2DModel(_x, _y, _yPred);
            }
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
            var loaded = _datasetReader.LoadAuto(path, HasHeadersCheckBox.IsChecked == true, previewRows: 200);
            _table = loaded.RawTable;
            _tableModified = false;

            PreviewGrid.ItemsSource = _table.DefaultView;

            _vm.Coefficients.Clear();
            _vm.Predictions.Clear();

            PopulateColumnCombos(_table);
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

    private void ApplyGridVisibility()
    {
        if (Plot2D.Model == null) return;
        var style = ShowGridCheckBox.IsChecked == true ? LineStyle.Solid : LineStyle.None;
        foreach (var axis in Plot2D.Model.Axes)
            axis.MajorGridlineStyle = style;
        Plot2D.InvalidatePlot(true);
    }

    private void ShowGridCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ApplyGridVisibility();
    }

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
}