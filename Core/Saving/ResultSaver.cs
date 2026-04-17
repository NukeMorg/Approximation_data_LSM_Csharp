using ClosedXML.Excel;
using Dapper;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

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
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is required", nameof(filePath));

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".json":
                throw new NotSupportedException("Use ModelPersistence for JSON model files.");
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

    public void SaveReportToExcel(
        string filePath,
        IReadOnlyList<double> coefficients,
        double mse,
        double r2Adjusted,
        IReadOnlyList<double> yPred,
        double[]? xValues = null,
        string sourceFile = "",
        string equation = "")
    {
        using var wb = new XLWorkbook();

        var wsInfo = wb.Worksheets.Add("Результаты");
        wsInfo.Cell(1, 1).Value = "Параметр";
        wsInfo.Cell(1, 2).Value = "Значение";
        wsInfo.Row(1).Style.Font.Bold = true;

        int row = 2;
        wsInfo.Cell(row++, 1).Value = "Дата и время";
        wsInfo.Cell(row - 1, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        wsInfo.Cell(row++, 1).Value = "Источник данных";
        wsInfo.Cell(row - 1, 2).Value = string.IsNullOrEmpty(sourceFile) ? "не указан" : sourceFile;
        wsInfo.Cell(row++, 1).Value = "Уравнение регрессии";
        wsInfo.Cell(row - 1, 2).Value = equation;
        wsInfo.Cell(row++, 1).Value = "Степень полинома";
        wsInfo.Cell(row - 1, 2).Value = coefficients.Count - 1;
        wsInfo.Cell(row++, 1).Value = "MSE";
        wsInfo.Cell(row - 1, 2).Value = mse;
        wsInfo.Cell(row++, 1).Value = "RMSE";
        wsInfo.Cell(row - 1, 2).Value = Math.Sqrt(mse);
        wsInfo.Cell(row++, 1).Value = "Скорректированный R²";
        wsInfo.Cell(row - 1, 2).Value = r2Adjusted;
        wsInfo.Columns().AdjustToContents();

        var wsCoef = wb.Worksheets.Add("Коэффициенты");
        wsCoef.Cell(1, 1).Value = "Индекс";
        wsCoef.Cell(1, 2).Value = "Коэффициент";
        wsCoef.Row(1).Style.Font.Bold = true;
        for (int i = 0; i < coefficients.Count; i++)
        {
            wsCoef.Cell(i + 2, 1).Value = i;
            wsCoef.Cell(i + 2, 2).Value = coefficients[i];
        }
        wsCoef.Columns().AdjustToContents();

        var wsPred = wb.Worksheets.Add("Предсказания");
        if (xValues != null && xValues.Length == yPred.Count)
        {
            wsPred.Cell(1, 1).Value = "X";
            wsPred.Cell(1, 2).Value = "Y (предсказанное)";
            wsPred.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < yPred.Count; i++)
            {
                wsPred.Cell(i + 2, 1).Value = xValues[i];
                wsPred.Cell(i + 2, 2).Value = yPred[i];
            }
        }
        else
        {
            wsPred.Cell(1, 1).Value = "Индекс";
            wsPred.Cell(1, 2).Value = "Y (предсказанное)";
            wsPred.Row(1).Style.Font.Bold = true;
            for (int i = 0; i < yPred.Count; i++)
            {
                wsPred.Cell(i + 2, 1).Value = i;
                wsPred.Cell(i + 2, 2).Value = yPred[i];
            }
        }
        wsPred.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void SaveReportToWord(
        string filePath,
        IReadOnlyList<double> coefficients,
        double mse,
        double r2Adjusted,
        IReadOnlyList<double> yPred,
        double[]? xValues = null,
        string sourceFile = "",
        string equation = "")
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        body.AppendChild(new Paragraph(new Run(new Text("ОТЧЁТ ПО РЕГРЕССИОННОЙ МОДЕЛИ")))
        {
            ParagraphProperties = new ParagraphProperties(new Justification() { Val = JustificationValues.Center })
        });
        body.AppendChild(new Paragraph(new Run(new Text(new string('=', 50)))));
        AddKeyValue(body, "Дата и время", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        AddKeyValue(body, "Источник данных", string.IsNullOrEmpty(sourceFile) ? "не указан" : sourceFile);
        AddKeyValue(body, "Уравнение регрессии", equation);
        AddKeyValue(body, "Степень полинома", (coefficients.Count - 1).ToString());
        AddKeyValue(body, "MSE", mse.ToString("F6", CultureInfo.InvariantCulture));
        AddKeyValue(body, "RMSE", Math.Sqrt(mse).ToString("F6", CultureInfo.InvariantCulture));
        AddKeyValue(body, "Скорректированный R²", r2Adjusted.ToString("F6", CultureInfo.InvariantCulture));
        body.AppendChild(new Paragraph());

        body.AppendChild(new Paragraph(new Run(new Text("КОЭФФИЦИЕНТЫ:"))));
        var tableCoef = CreateTable(new[] { "Индекс", "Коэффициент" });
        for (int i = 0; i < coefficients.Count; i++)
            AddTableRow(tableCoef, i.ToString(), coefficients[i].ToString("G10", CultureInfo.InvariantCulture));
        body.AppendChild(tableCoef);
        body.AppendChild(new Paragraph());

        body.AppendChild(new Paragraph(new Run(new Text("ПРЕДСКАЗАННЫЕ ЗНАЧЕНИЯ:"))));
        Table tablePred;
        if (xValues != null && xValues.Length == yPred.Count)
        {
            tablePred = CreateTable(new[] { "X", "Y (предсказанное)" });
            for (int i = 0; i < yPred.Count; i++)
                AddTableRow(tablePred, xValues[i].ToString(CultureInfo.InvariantCulture), yPred[i].ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            tablePred = CreateTable(new[] { "Индекс", "Y (предсказанное)" });
            for (int i = 0; i < yPred.Count; i++)
                AddTableRow(tablePred, i.ToString(), yPred[i].ToString(CultureInfo.InvariantCulture));
        }
        body.AppendChild(tablePred);
        doc.Save();
    }

    private static void AddKeyValue(Body body, string key, string value)
    {
        var para = new Paragraph();
        para.AppendChild(new Run(new Text(key + ": ")) { RunProperties = new RunProperties(new Bold()) });
        para.AppendChild(new Run(new Text(value)));
        body.AppendChild(para);
    }

    private static Table CreateTable(string[] headers)
    {
        var table = new Table();
        var tableProps = new TableProperties(
            new TableBorders(
                new TopBorder() { Val = BorderValues.Single, Size = 4 },
                new BottomBorder() { Val = BorderValues.Single, Size = 4 },
                new LeftBorder() { Val = BorderValues.Single, Size = 4 },
                new RightBorder() { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder() { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder() { Val = BorderValues.Single, Size = 4 }
            )
        );
        table.AppendChild(tableProps);
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            var cell = new TableCell(new Paragraph(new Run(new Text(h))));
            cell.AppendChild(new TableCellProperties(new Shading() { Fill = "D9D9D9" }));
            headerRow.AppendChild(cell);
        }
        table.AppendChild(headerRow);
        return table;
    }

    private static void AddTableRow(Table table, params string[] cells)
    {
        var row = new TableRow();
        foreach (var c in cells)
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(c)))));
        table.AppendChild(row);
    }

    private static void SaveAsTxt(string filePath, IReadOnlyList<double> coefficients, double mse, double r2Adjusted,
        IReadOnlyList<double> yPred)
    {
        var sb = new StringBuilder();
        sb.AppendLine("РЕЗУЛЬТАТЫ МЕТОДА НАИМЕНЬШИХ КВАДРАТОВ");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();
        sb.AppendLine($"Коэффициенты полинома: [{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]");
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
        sw.WriteLine($"Коэффициенты,\"[{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]\"");
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
        ws1.Cell(2, 2).Value = $"[{string.Join(", ", coefficients.Select(c => c.ToString("G17", CultureInfo.InvariantCulture)))}]";
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
            new { count = coefficients.Count, mse, r2 = r2Adjusted, createdAt, sourceFile }, tx);
        var coeffsJson = JsonSerializer.Serialize(coefficients.ToArray());
        var predsJson = JsonSerializer.Serialize(yPred.ToArray());
        conn.Execute("INSERT INTO coefficients (result_id, all_coefficients) VALUES (@id, @json)", new { id = resultId, json = coeffsJson }, tx);
        conn.Execute("INSERT INTO predictions (result_id, all_predictions) VALUES (@id, @json)", new { id = resultId, json = predsJson }, tx);
        tx.Commit();
    }
}