using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
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
    private double[]? _z;

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

            // Update VM
            _vm.MSE = _metrics.Value.Mse;
            _vm.RMSE = Math.Sqrt(_vm.MSE);
            _vm.AdjustedR2 = _metrics.Value.AdjustedR2;
            _vm.R2 = _vm.AdjustedR2; // пока без отдельного R2
            _vm.RegressionEquation = FormatEquation(_coeffs);
            _vm.StatusText = $"OK ({method}). MSE={_vm.MSE:F4}, AdjR²={_vm.AdjustedR2:F4}";

            // Plot
            Plot2D.Model = Plot2DModel(_x, _y, _yPred);
            //if (_z is not null)
            //    Plot3DPoints(_x, _y, _z);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleMode_OnChanged(object sender, RoutedEventArgs e)
    {
        // Can fire during InitializeComponent when some named fields are not yet assigned.
        if (Toggle2D is null || Toggle3D is null || Plot2D is null || Plot3D is null)
            return;

        // mutually exclusive
        if (sender == Toggle2D && Toggle2D.IsChecked == true) Toggle3D.IsChecked = false;
        if (sender == Toggle3D && Toggle3D.IsChecked == true) Toggle2D.IsChecked = false;

        ApplyPlotMode();
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
            //FullDataGrid.ItemsSource = _table.DefaultView;

            PopulateColumnCombos(_table);
            LoadArraysFromTable();

            // initial plot
            if (_x is not null && _y is not null)
                Plot2D.Model = Plot2DModel(_x, _y, yPred: null);
            //if (_z is not null && _x is not null && _y is not null)
            //    Plot3DPoints(_x, _y, _z);

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

        // prefer named columns
        XColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("x", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(0);
        YColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)) ?? names.ElementAtOrDefault(1);
        ZColumnCombo.SelectedItem = names.FirstOrDefault(n => n.Trim().Equals("z", StringComparison.OrdinalIgnoreCase)) ?? (names.Count >= 3 ? names[2] : null);
    }

    private void LoadArraysFromTable()
    {
        if (_table is null) return;

        // Use combos if selected, otherwise fallback to auto logic
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
        if (_coeffs is null || _yPred is null || _metrics is null)
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
        _resultSaver.SaveResults(dlg.FileName, _coeffs, _metrics.Value.Mse, _metrics.Value.AdjustedR2, _yPred, _sourcePath ?? "");
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

    //private void Plot3DPoints(double[] x, double[] y, double[] z)
    //{
    //    Plot3D.Children.Clear();
    //    Plot3D.Children.Add(new DefaultLights());

    //    var group = new System.Windows.Media.Media3D.Model3DGroup();
    //    var builder = new MeshBuilder(false, false);
    //    for (var i = 0; i < x.Length && i < y.Length && i < z.Length; i++)
    //        builder.AddSphere(new System.Windows.Media.Media3D.Point3D(x[i], y[i], z[i]), radius: 0.05, thetaDiv: 6, phiDiv: 6);

    //    var mesh = builder.ToMesh(true);
    //    group.Children.Add(new System.Windows.Media.Media3D.GeometryModel3D(mesh, Materials.Blue));
    //    Plot3D.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D { Content = group });
    //}
}

