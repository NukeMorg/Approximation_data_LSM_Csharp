using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CurseWork.Core.Saving;

public sealed class ResultSaver
{
    public void SaveResults(
        string filePath,
        IReadOnlyList<double> coefficients,
        double mse,
        double r2Adjusted,
        IReadOnlyList<double> yPred,
        string sourceFile = "")
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required", nameof(filePath));

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".txt":
                SaveAsTxt(filePath, coefficients, mse, r2Adjusted, yPred);
                break;
            case ".csv":
                SaveAsCsv(filePath, coefficients, mse, r2Adjusted, yPred);
                break;
            case ".xlsx":
                SaveAsExcel(filePath, coefficients, mse, r2Adjusted, yPred);
                break;
            case ".db":
            case ".sqlite":
                SaveToDb(filePath, coefficients, mse, r2Adjusted, yPred, sourceFile);
                break;
            default:
                SaveAsTxt(filePath, coefficients, mse, r2Adjusted, yPred);
                break;
        }
    }

    private static void SaveAsTxt(string filePath, IReadOnlyList<double> coefficients, double mse, double r2Adjusted,
        IReadOnlyList<double> yPred)
    {
        var sb = new StringBuilder();
        sb.AppendLine("РЕЗУЛЬТАТЫ МЕТОДА НАИМЕНЬШИХ КВАДРАТОВ");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();
        sb.AppendLine(
            $"Коэффициенты полинома: [{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]");
        sb.AppendLine($"Среднеквадратичная ошибка (MSE): {mse.ToString("F6", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Скорректированный R²: {r2Adjusted.ToString("F6", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("Предсказанные значения:");
        for (var i = 0; i < yPred.Count; i++)
            sb.AppendLine($"y_pred[{i}] = {yPred[i].ToString("F6", CultureInfo.InvariantCulture)}");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static void SaveAsCsv(string filePath, IReadOnlyList<double> coefficients, double mse, double r2Adjusted,
        IReadOnlyList<double> yPred)
    {
        using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
        sw.WriteLine("Параметр,Значение");
        sw.WriteLine(
            $"Коэффициенты,\"[{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]\"");
        sw.WriteLine($"MSE,{mse.ToString("F6", CultureInfo.InvariantCulture)}");
        sw.WriteLine($"R2_Adjusted,{r2Adjusted.ToString("F6", CultureInfo.InvariantCulture)}");
        sw.WriteLine();
        sw.WriteLine("Индекс,Предсказанное_значение");
        for (var i = 0; i < yPred.Count; i++)
            sw.WriteLine($"{i},{yPred[i].ToString("F6", CultureInfo.InvariantCulture)}");
    }

    private static void SaveAsExcel(string filePath, IReadOnlyList<double> coefficients, double mse, double r2Adjusted,
        IReadOnlyList<double> yPred)
    {
        using var wb = new XLWorkbook();
        var ws1 = wb.Worksheets.Add("Результаты");
        ws1.Cell(1, 1).Value = "Параметр";
        ws1.Cell(1, 2).Value = "Значение";
        ws1.Row(1).Style.Font.Bold = true;
        ws1.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws1.Cell(2, 1).Value = "Коэффициенты";
        ws1.Cell(2, 2).Value =
            $"[{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]";
        ws1.Cell(3, 1).Value = "MSE";
        ws1.Cell(3, 2).Value = mse;
        ws1.Cell(4, 1).Value = "R2_Adjusted";
        ws1.Cell(4, 2).Value = r2Adjusted;
        ws1.Columns().AdjustToContents();

        var ws2 = wb.Worksheets.Add("Предсказания");
        ws2.Cell(1, 1).Value = "Индекс";
        ws2.Cell(1, 2).Value = "Предсказанное_значение";
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        for (var i = 0; i < yPred.Count; i++)
        {
            ws2.Cell(i + 2, 1).Value = i;
            ws2.Cell(i + 2, 2).Value = yPred[i];
        }
        ws2.Columns().AdjustToContents();

        wb.SaveAs(filePath);
    }

    private static void SaveToDb(string filePath, IReadOnlyList<double> coefficients, double mse, double r2Adjusted,
        IReadOnlyList<double> yPred, string sourceFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        using var conn = new SqliteConnection($"Data Source={filePath};Mode=ReadWriteCreate;");
        conn.Open();

        conn.Execute("""
CREATE TABLE IF NOT EXISTS lsm_results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  coefficients_count INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  mse REAL NOT NULL,
  r2 REAL NOT NULL,
  source_file TEXT
);
""");

        conn.Execute("""
CREATE TABLE IF NOT EXISTS coefficients (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  result_id INTEGER NOT NULL,
  all_coefficients TEXT NOT NULL,
  FOREIGN KEY(result_id) REFERENCES lsm_results(id) ON DELETE CASCADE
);
""");

        conn.Execute("""
CREATE TABLE IF NOT EXISTS predictions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  result_id INTEGER NOT NULL,
  all_predictions TEXT NOT NULL,
  FOREIGN KEY(result_id) REFERENCES lsm_results(id) ON DELETE CASCADE
);
""");

        using var tx = conn.BeginTransaction();
        var createdAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var resultId = conn.ExecuteScalar<long>(
            """
INSERT INTO lsm_results (coefficients_count, mse, r2, created_at, source_file)
VALUES (@count, @mse, @r2, @createdAt, @sourceFile);
SELECT last_insert_rowid();
""",
            new
            {
                count = coefficients.Count,
                mse,
                r2 = r2Adjusted,
                createdAt,
                sourceFile
            },
            tx
        );

        var coeffsJson = JsonSerializer.Serialize(coefficients.ToArray());
        var predsJson = JsonSerializer.Serialize(yPred.ToArray());

        conn.Execute(
            "INSERT INTO coefficients (result_id, all_coefficients) VALUES (@id, @json)",
            new { id = resultId, json = coeffsJson }, tx);

        conn.Execute(
            "INSERT INTO predictions (result_id, all_predictions) VALUES (@id, @json)",
            new { id = resultId, json = predsJson }, tx);

        tx.Commit();
    }
}

