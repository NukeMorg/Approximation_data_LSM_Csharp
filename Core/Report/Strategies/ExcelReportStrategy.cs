using System;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class ExcelReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            // Обязательно для некоммерческого использования
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();

            // Лист "Результаты"
            var wsInfo = package.Workbook.Worksheets.Add("Результаты");
            wsInfo.Cells[1, 1].Value = "Параметр";
            wsInfo.Cells[1, 2].Value = "Значение";
            wsInfo.Row(1).Style.Font.Bold = true;

            int row = 2;
            wsInfo.Cells[row++, 1].Value = "Дата и время";
            wsInfo.Cells[row - 1, 2].Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            wsInfo.Cells[row++, 1].Value = "Уравнение";
            wsInfo.Cells[row - 1, 2].Value = result.Equation;
            wsInfo.Cells[row++, 1].Value = "MSE";
            wsInfo.Cells[row - 1, 2].Value = result.Metrics.Mse;
            wsInfo.Cells[row++, 1].Value = "RMSE";
            wsInfo.Cells[row - 1, 2].Value = Math.Sqrt(result.Metrics.Mse);
            wsInfo.Cells[row++, 1].Value = "Скорректированный R²";
            wsInfo.Cells[row - 1, 2].Value = result.Metrics.AdjustedR2;
            wsInfo.Column(1).AutoFit();
            wsInfo.Column(2).AutoFit();

            // Коэффициенты
            var wsCoef = package.Workbook.Worksheets.Add("Коэффициенты");
            wsCoef.Cells[1, 1].Value = "Индекс";
            wsCoef.Cells[1, 2].Value = "Значение";
            wsCoef.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < result.Coefficients.Count; i++)
            {
                wsCoef.Cells[i + 2, 1].Value = i;
                wsCoef.Cells[i + 2, 2].Value = result.Coefficients[i];
            }
            wsCoef.Column(1).AutoFit();
            wsCoef.Column(2).AutoFit();

            // Предсказания
            var wsPred = package.Workbook.Worksheets.Add("Предсказания");
            if (result.Is3D)
            {
                wsPred.Cells[1, 1].Value = "X";
                wsPred.Cells[1, 2].Value = "Y";
                wsPred.Cells[1, 3].Value = "Z (предск.)";
                wsPred.Row(1).Style.Font.Bold = true;
                for (int i = 0; i < result.Predictions.Count; i++)
                {
                    var p = result.Predictions[i];
                    wsPred.Cells[i + 2, 1].Value = p.X;
                    wsPred.Cells[i + 2, 2].Value = p.Y;
                    wsPred.Cells[i + 2, 3].Value = p.Z ?? 0.0;
                }
            }
            else
            {
                wsPred.Cells[1, 1].Value = "X";
                wsPred.Cells[1, 2].Value = "Y (предск.)";
                wsPred.Row(1).Style.Font.Bold = true;
                for (int i = 0; i < result.Predictions.Count; i++)
                {
                    var p = result.Predictions[i];
                    wsPred.Cells[i + 2, 1].Value = p.X;
                    wsPred.Cells[i + 2, 2].Value = p.Y;
                }
            }
            wsPred.Column(1).AutoFit();
            wsPred.Column(2).AutoFit();
            wsPred.Column(3).AutoFit();

            // График только для 2D
            if (!result.Is3D)
            {
                var wsChart = package.Workbook.Worksheets.Add("График");

                // Записываем данные для графика
                wsChart.Cells[1, 1].Value = "X";
                wsChart.Cells[1, 2].Value = "Y (предск.)";
                for (int i = 0; i < result.Predictions.Count; i++)
                {
                    wsChart.Cells[i + 2, 1].Value = result.Predictions[i].X;
                    wsChart.Cells[i + 2, 2].Value = result.Predictions[i].Y;
                }

                // Создаём точечную диаграмму с гладкими линиями
                var chart = wsChart.Drawings.AddChart("ModelChart", eChartType.XYScatterSmoothNoMarkers);
                chart.Title.Text = "График модели";
                chart.SetPosition(0, 5, 3, 5);   // строка, смещение в пикселях от верхней границы, столбец, смещение от левой границы
                chart.SetSize(800, 500);

                var xRange = wsChart.Cells[$"A2:A{result.Predictions.Count + 1}"];
                var yRange = wsChart.Cells[$"B2:B{result.Predictions.Count + 1}"];

                var series = chart.Series.Add(yRange, xRange);
                series.Header = "Модель";
            }

            package.SaveAs(new FileInfo(filePath));
        }
    }
}