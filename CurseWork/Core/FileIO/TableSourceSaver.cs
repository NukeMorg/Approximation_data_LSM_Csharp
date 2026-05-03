using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CurseWork.Core.FileIO;

public sealed class TableSourceSaver
{
    public void Save(DataTable table, string sourcePath)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("sourcePath is required", nameof(sourcePath));

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        switch (ext)
        {
            case ".csv":
                SaveDelimited(table, sourcePath, delimiter: ",");
                return;
            case ".txt":
                SaveTxtPreservingDelimiterIfPossible(table, sourcePath);
                return;
            case ".xlsx":
                SaveExcel(table, sourcePath);
                return;
            case ".db":
            case ".sqlite":
                SaveSqliteFirstTable(table, sourcePath);
                return;
            default:
                throw new NotSupportedException($"Сохранение в формат {ext} не поддерживается");
        }
    }

    private static void SaveTxtPreservingDelimiterIfPossible(DataTable table, string filePath)
    {
        var delim = ",";
        try
        {
            using var sr = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var first = sr.ReadLine() ?? "";
            if (first.Contains(';')) delim = ";";
            else if (first.Contains('\t')) delim = "\t";
            else if (first.Contains(',')) delim = ",";
            else if (first.Contains(' ')) delim = " ";
        }
        catch
        {
            // ignore and use default ','
        }

        SaveDelimited(table, filePath, delim);
    }

    private static void SaveDelimited(DataTable table, string filePath, string delimiter)
    {
        using var sw = new StreamWriter(filePath, false, Encoding.UTF8);

        sw.WriteLine(string.Join(delimiter, table.Columns.Cast<DataColumn>().Select(c => EscapeCell(c.ColumnName))));

        foreach (DataRow row in table.Rows)
        {
            var cells = new string[table.Columns.Count];
            for (var i = 0; i < table.Columns.Count; i++)
                cells[i] = EscapeCell(row[i]?.ToString() ?? "");
            sw.WriteLine(string.Join(delimiter, cells));
        }

        static string EscapeCell(string s)
        {
            if (s.Contains('"') || s.Contains(',') || s.Contains(';') || s.Contains('\n') || s.Contains('\r') || s.Contains('\t'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }

    private static void SaveExcel(DataTable table, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Data");

        for (var c = 0; c < table.Columns.Count; c++)
            ws.Cell(1, c + 1).Value = table.Columns[c].ColumnName;

        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Columns.Count; c++)
                ws.Cell(r + 2, c + 1).Value = table.Rows[r][c]?.ToString() ?? "";
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    private static void SaveSqliteFirstTable(DataTable table, string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;");
        conn.Open();

        var tableName = conn.ExecuteScalar<string?>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1");
        if (string.IsNullOrWhiteSpace(tableName))
            throw new InvalidOperationException("В базе данных нет таблиц для сохранения.");

        using var tx = conn.BeginTransaction();

        conn.Execute($"DELETE FROM {tableName}", transaction: tx);

        var cols = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        var colList = string.Join(", ", cols.Select(QuoteIdent));
        var paramList = string.Join(", ", cols.Select(c => "@" + c));
        var insertSql = $"INSERT INTO {tableName} ({colList}) VALUES ({paramList})";

        foreach (DataRow row in table.Rows)
        {
            var dyn = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cols.Length; i++)
                dyn[cols[i]] = row[i]?.ToString();
            conn.Execute(insertSql, dyn, transaction: tx);
        }

        tx.Commit();

        static string QuoteIdent(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";
    }
}

