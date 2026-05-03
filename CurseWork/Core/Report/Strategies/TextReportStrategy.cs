using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class TextReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".csv")
                SaveCsv(filePath, result);
            else
                SaveTxt(filePath, result);
        }

        private static void SaveTxt(string filePath, IRegressionResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ОТЧЁТ ПО РЕГРЕССИОННОЙ МОДЕЛИ");
            sb.AppendLine(new string('=', 50));
            sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Режим: {(result.Is3D ? "3D" : "2D")}");
            sb.AppendLine($"Уравнение: {result.Equation}");
            sb.AppendLine($"MSE: {result.Metrics.Mse:F6}");
            sb.AppendLine($"RMSE: {Math.Sqrt(result.Metrics.Mse):F6}");
            sb.AppendLine($"Скорр. R²: {result.Metrics.AdjustedR2:F6}");
            sb.AppendLine();
            sb.AppendLine("Коэффициенты:");
            for (int i = 0; i < result.Coefficients.Count; i++)
                sb.AppendLine($"  a{i} = {result.Coefficients[i]:G10}");
            sb.AppendLine();
            sb.AppendLine("Предсказания:");
            if (result.Is3D)
            {
                foreach (var p in result.Predictions)
                    sb.AppendLine($"X={p.X:F6}  Y={p.Y:F6}  Zpred={p.Z:F6}");
            }
            else
            {
                foreach (var p in result.Predictions)
                    sb.AppendLine($"X={p.X:F6}  Ypred={p.Y:F6}");
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static void SaveCsv(string filePath, IRegressionResult result)
        {
            using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
            sw.WriteLine("Parameter,Value");
            sw.WriteLine($"Дата,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine($"Уравнение,\"{result.Equation}\"");
            sw.WriteLine($"MSE,{result.Metrics.Mse:F6}");
            sw.WriteLine($"RMSE,{Math.Sqrt(result.Metrics.Mse):F6}");
            sw.WriteLine($"AdjustedR2,{result.Metrics.AdjustedR2:F6}");
            sw.WriteLine();
            sw.WriteLine("Индекс,Коэффициент");
            for (int i = 0; i < result.Coefficients.Count; i++)
                sw.WriteLine($"{i},{result.Coefficients[i]:G10}");
            sw.WriteLine();
            if (result.Is3D)
            {
                sw.WriteLine("X,Y,Z_pred");
                foreach (var p in result.Predictions)
                    sw.WriteLine($"{p.X},{p.Y},{p.Z}");
            }
            else
            {
                sw.WriteLine("X,Y_pred");
                foreach (var p in result.Predictions)
                    sw.WriteLine($"{p.X},{p.Y}");
            }
        }
    }
}