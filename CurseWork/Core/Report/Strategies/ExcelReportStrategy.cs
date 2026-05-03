using OfficeOpenXml;
using OfficeOpenXml.Style;
using OfficeOpenXml.Drawing.Chart;
using System;
using System.IO;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class ExcelReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);


            using var package = new ExcelPackage(new FileInfo(filePath));

            // ================= РЕЗУЛЬТАТЫ =================
            var wsInfo = package.Workbook.Worksheets.Add("Результаты");

            wsInfo.Cells["A1"].Value = "Параметр";
            wsInfo.Cells["B1"].Value = "Значение";

            wsInfo.Cells["A1:B1"].Style.Font.Bold = true;

            int r = 2;
            wsInfo.Cells[r, 1].Value = "Дата и время"; wsInfo.Cells[r++, 2].Value = DateTime.Now;
            wsInfo.Cells[r, 1].Value = "Уравнение"; wsInfo.Cells[r++, 2].Value = result.Equation;
            wsInfo.Cells[r, 1].Value = "MSE"; wsInfo.Cells[r++, 2].Value = result.Metrics.Mse;
            wsInfo.Cells[r, 1].Value = "RMSE"; wsInfo.Cells[r++, 2].Value = Math.Sqrt(result.Metrics.Mse);
            wsInfo.Cells[r, 1].Value = "R² (скорр.)"; wsInfo.Cells[r++, 2].Value = result.Metrics.AdjustedR2;

            wsInfo.Cells.AutoFitColumns();

            // ================= КОЭФФИЦИЕНТЫ =================
            var wsCoef = package.Workbook.Worksheets.Add("Коэффициенты");

            wsCoef.Cells["A1"].Value = "Индекс";
            wsCoef.Cells["B1"].Value = "Значение";
            wsCoef.Cells["A1:B1"].Style.Font.Bold = true;

            for (int i = 0; i < result.Coefficients.Count; i++)
            {
                wsCoef.Cells[i + 2, 1].Value = i;
                wsCoef.Cells[i + 2, 2].Value = result.Coefficients[i];
            }

            wsCoef.Cells.AutoFitColumns();

            // ================= ПРЕДСКАЗАНИЯ =================
            var wsPred = package.Workbook.Worksheets.Add("Предсказания");

            wsPred.Cells["A1"].Value = "X";
            wsPred.Cells["B1"].Value = "Y";
            wsPred.Cells["A1:B1"].Style.Font.Bold = true;

            int count = result.Predictions.Count;

            for (int i = 0; i < count; i++)
            {
                var p = result.Predictions[i];
                wsPred.Cells[i + 2, 1].Value = p.X;
                wsPred.Cells[i + 2, 2].Value = p.Y;
            }

            wsPred.Cells.AutoFitColumns();

            // ================= ГРАФИК =================
            if (!result.Is3D && count > 0)
            {
                var wsChart = package.Workbook.Worksheets.Add("График");

                // копируем данные
                wsChart.Cells["A1"].Value = "X";
                wsChart.Cells["B1"].Value = "Y";

                for (int i = 0; i < count; i++)
                {
                    var p = result.Predictions[i];
                    wsChart.Cells[i + 2, 1].Value = p.X;
                    wsChart.Cells[i + 2, 2].Value = p.Y;
                }

                var chart = wsChart.Drawings.AddChart("chart", eChartType.XYScatterLines);

                chart.Title.Text = "График модели";

                var series = chart.Series.Add(
                    wsChart.Cells[2, 2, count + 1, 2], // Y
                    wsChart.Cells[2, 1, count + 1, 1]  // X
                );

                chart.SetPosition(1, 0, 2, 0);
                chart.SetSize(800, 500);

                chart.XAxis.Title.Text = "X";
                chart.YAxis.Title.Text = "Y";
            }

            package.Save();
        }
    }
}