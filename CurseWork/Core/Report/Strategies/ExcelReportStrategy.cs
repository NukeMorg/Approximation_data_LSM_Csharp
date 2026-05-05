using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using System;
using System.IO;
using System.Linq;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class ExcelReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            // EPPlus 8+ лицензия (обязательно один раз в приложении, но оставил здесь для автономности)
            ExcelPackage.License.SetNonCommercialPersonal("CurseWork");

            using var package = new ExcelPackage(new FileInfo(filePath));

            // ================= 1. РЕЗУЛЬТАТЫ =================
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

            // ================= 2. КОЭФФИЦИЕНТЫ =================
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

            // ================= 3. ПРЕДСКАЗАНИЯ =================
            var wsPred = package.Workbook.Worksheets.Add("Предсказания");

            wsPred.Cells["A1"].Value = "X";
            wsPred.Cells["B1"].Value = "Y";
            wsPred.Cells["A1:B1"].Style.Font.Bold = true;

            int count = result.Predictions.Count;

            for (int i = 0; i < count; i++)
            {
                wsPred.Cells[i + 2, 1].Value = result.Predictions[i].X;
                wsPred.Cells[i + 2, 2].Value = result.Predictions[i].Y;
            }

            wsPred.Cells.AutoFitColumns();

            // ================= 4. ГРАФИК (РАБОЧИЙ ВАРИАНТ) =================
            if (!result.Is3D && count > 0)
            {
                // 1) Лист с данными (скрытый)
                var wsData = package.Workbook.Worksheets.Add("ChartData");

                for (int i = 0; i < count; i++)
                {
                    wsData.Cells[i + 1, 1].Value = result.Predictions[i].X;
                    wsData.Cells[i + 1, 2].Value = result.Predictions[i].Y;
                }

                wsData.Hidden = eWorkSheetHidden.VeryHidden;

                // 2) Лист графика (пустой визуально)
                var wsChart = package.Workbook.Worksheets.Add("График");

                var chart = wsChart.Drawings.AddChart("RegressionChart", eChartType.XYScatterLines);

                chart.Title.Text = "График модели";
                chart.SetPosition(1, 0, 1, 0);
                chart.SetSize(900, 500);

                chart.XAxis.Title.Text = "X";
                chart.YAxis.Title.Text = "Y";

                // ВАЖНО: ссылка на ДРУГОЙ лист
                var xRange = wsData.Cells[1, 1, count, 1];
                var yRange = wsData.Cells[1, 2, count, 2];

                var series = chart.Series.Add(yRange, xRange);
                series.Header = "Модель";
            }

            package.Save();
        }
    }
}