using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace CurseWork.Core.FileIO;

public sealed record DatasetLoadResult(bool Success, string Message, DataTable RawTable);
public sealed record Dataset3DResult(double[] X, double[] Y, double[] Z);

public sealed class DatasetReader
{
    public DatasetLoadResult LoadAuto(string filePath, int? previewRows = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Путь к файлу не указан.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Файл не найден: {filePath}", filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".csv" or ".txt" => LoadText(filePath, previewRows),
            ".xlsx" => LoadExcel(filePath, previewRows),
            ".db" or ".sqlite" => LoadSqlite(filePath, previewRows),
            _ => throw new NotSupportedException($"Неподдерживаемый формат: {ext}")
        };
    }

    public DatasetLoadResult LoadText(string filePath, int? previewRows = null)
    {
        var sample = ReadNonEmptyLines(filePath, maxLines: 5);
        var sep = GuessSeparator(sample);

        var table = new DataTable();
        var rowIndex = 0;
        foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
        {
            var trimmed = line?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var fields = SplitDelimitedLine(trimmed, sep);
            while (table.Columns.Count < fields.Length)
                table.Columns.Add($"col{table.Columns.Count}", typeof(string));

            var row = table.NewRow();
            for (var i = 0; i < fields.Length; i++)
                row[i] = fields[i];
            table.Rows.Add(row);

            rowIndex++;
            if (previewRows is not null && rowIndex >= previewRows.Value)
                break;
        }

        return new DatasetLoadResult(true, $"✅ Файл успешно загружен ({table.Rows.Count} строк)", table);
    }

    public DatasetLoadResult LoadExcel(string filePath, int? previewRows = null)
    {
        using var wb = new XLWorkbook(filePath);
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        if (range is null)
            return new DatasetLoadResult(false, "❌ Excel пуст", new DataTable());

        var table = new DataTable();
        var rows = range.RowsUsed().ToList();
        if (rows.Count == 0)
            return new DatasetLoadResult(false, "❌ Excel пуст", table);

        var headerRow = rows[0];
        var colCount = headerRow.CellsUsed().Count();
        if (colCount == 0) colCount = range.ColumnCount();
        for (var c = 1; c <= colCount; c++)
        {
            var name = headerRow.Cell(c).GetString();
            if (string.IsNullOrWhiteSpace(name)) name = $"col{c - 1}";
            table.Columns.Add(name, typeof(string));
        }

        var max = previewRows is null ? rows.Count - 1 : Math.Min(rows.Count - 1, previewRows.Value);
        for (var r = 1; r <= max; r++)
        {
            var row = table.NewRow();
            for (var c = 1; c <= colCount; c++)
                row[c - 1] = rows[r].Cell(c).GetString();
            table.Rows.Add(row);
        }

        return new DatasetLoadResult(true, $"✅ Excel загружен ({table.Rows.Count} строк)", table);
    }

    public DatasetLoadResult LoadSqlite(string dbPathOrQuery, int? previewRows = null, string? dbPath = null)
    {
        var isQuery = LooksLikeSqlQuery(dbPathOrQuery);
        var actualDbPath = isQuery ? (dbPath ?? dbPathOrQuery) : dbPathOrQuery;
        var query = isQuery ? dbPathOrQuery : null;

        if (!File.Exists(actualDbPath))
            throw new FileNotFoundException($"Файл БД не найден: {actualDbPath}", actualDbPath);

        using var conn = new SqliteConnection($"Data Source={actualDbPath};Mode=ReadWriteCreate;");
        conn.Open();

        if (query is null)
        {
            var tableName = GetFirstTableName(conn) ?? throw new InvalidOperationException("❌ Нет таблиц в БД");
            query = $"SELECT * FROM {tableName}";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = query;
        using var rdr = cmd.ExecuteReader();

        var table = new DataTable();
        table.Load(rdr);

        if (previewRows is not null && table.Rows.Count > previewRows.Value)
        {
            while (table.Rows.Count > previewRows.Value)
                table.Rows.RemoveAt(table.Rows.Count - 1);
        }

        return new DatasetLoadResult(true, $"✅ Данные загружены: {table.Rows.Count} строк", table);
    }

    public static (double[] X, double[] Y) PrepareXY(DataTable table)
    {
        if (table.Columns.Count < 2)
            throw new InvalidOperationException("Ожидаются минимум 2 столбца (X и Y)");

        var xCol = table.Columns[0];
        var yCol = table.Columns[1];

        foreach (DataColumn col in table.Columns)
        {
            var name = col.ColumnName.Trim().ToLowerInvariant();
            if (name is "x" or "x_column" or "x_value" or "ось_x") xCol = col;
            else if (name is "y" or "y_column" or "y_value" or "ось_y") yCol = col;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (DataRow row in table.Rows)
        {
            if (!TryParseCell(row[xCol], out var x)) continue;
            if (!TryParseCell(row[yCol], out var y)) continue;
            xs.Add(x);
            ys.Add(y);
        }

        if (xs.Count < 2)
            throw new InvalidOperationException("Недостаточно числовых точек для анализа");

        return (xs.ToArray(), ys.ToArray());
    }

    public static Dataset3DResult PrepareXYZ(DataTable table)
    {
        if (table.Columns.Count < 3)
            throw new InvalidOperationException("Ожидаются минимум 3 столбца (X, Y и Z)");

        var xCol = table.Columns[0];
        var yCol = table.Columns[1];
        var zCol = table.Columns[2];

        foreach (DataColumn col in table.Columns)
        {
            var name = col.ColumnName.Trim().ToLowerInvariant();
            if (name is "x" or "x_column" or "x_value" or "ось_x") xCol = col;
            else if (name is "y" or "y_column" or "y_value" or "ось_y") yCol = col;
            else if (name is "z" or "z_column" or "z_value" or "ось_z") zCol = col;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        var zs = new List<double>();
        foreach (DataRow row in table.Rows)
        {
            if (!TryParseCell(row[xCol], out var x)) continue;
            if (!TryParseCell(row[yCol], out var y)) continue;
            if (!TryParseCell(row[zCol], out var z)) continue;
            xs.Add(x);
            ys.Add(y);
            zs.Add(z);
        }

        if (xs.Count < 2)
            throw new InvalidOperationException("Недостаточно числовых 3D-точек для анализа");

        return new Dataset3DResult(xs.ToArray(), ys.ToArray(), zs.ToArray());
    }

    public static double[] LoadVector(string filePath, int? expectedLength = null)
    {
        var reader = new DatasetReader();
        var loaded = reader.LoadAuto(filePath);
        var table = loaded.RawTable;
        if (table.Columns.Count == 0 || table.Rows.Count == 0)
            throw new InvalidOperationException("Файл весов пуст");

        var values = new List<double>();
        for (var r = 0; r < table.Rows.Count; r++)
            if (TryParseCell(table.Rows[r][0], out var v))
                values.Add(v);

        if (values.Count == 0)
            throw new InvalidOperationException("Не найдены числовые данные весов");

        if (expectedLength is not null)
        {
            if (values.Count > expectedLength.Value)
                values = values.Take(expectedLength.Value).ToList();
            while (values.Count < expectedLength.Value)
                values.Add(1.0);
        }

        return values.ToArray();
    }

    public static double[,] LoadMatrix(string filePath, int? expectedSize = null)
    {
        var reader = new DatasetReader();
        var loaded = reader.LoadAuto(filePath);
        var table = loaded.RawTable;
        if (table.Columns.Count == 0 || table.Rows.Count == 0)
            throw new InvalidOperationException("Файл матрицы пуст");

        var rows = new List<double[]>();
        foreach (DataRow row in table.Rows)
        {
            var vals = new List<double>();
            for (var c = 0; c < table.Columns.Count; c++)
                if (TryParseCell(row[c], out var v))
                    vals.Add(v);
            if (vals.Count > 0)
                rows.Add(vals.ToArray());
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("Не найдены числовые данные матрицы");

        var rowCount = rows.Count;
        var colCount = rows.Max(r => r.Length);
        var matrix = new double[rowCount, colCount];
        for (var i = 0; i < rowCount; i++)
        for (var j = 0; j < rows[i].Length; j++)
            matrix[i, j] = rows[i][j];

        if (expectedSize is not null)
        {
            var n = expectedSize.Value;
            if (rowCount < n || colCount < n)
                throw new InvalidOperationException($"Некорректная размерность матрицы: ({rowCount}, {colCount}), ожидалось минимум ({n}, {n})");

            var trimmed = new double[n, n];
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                trimmed[i, j] = matrix[i, j];

            Symmetrize(trimmed);
            return trimmed;
        }

        Symmetrize(matrix);
        return matrix;
    }

    private static bool TryParseCell(object? value, out double result)
    {
        var raw = value?.ToString() ?? string.Empty;
        raw = raw.Replace(',', '.').Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static void Symmetrize(double[,] matrix)
    {
        if (matrix.GetLength(0) != matrix.GetLength(1))
            return;
        var n = matrix.GetLength(0);
        for (var i = 0; i < n; i++)
        for (var j = i + 1; j < n; j++)
        {
            var avg = (matrix[i, j] + matrix[j, i]) / 2.0;
            matrix[i, j] = avg;
            matrix[j, i] = avg;
        }
    }

    private static string ReadNonEmptyLines(string path, int maxLines)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var line in File.ReadLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(t);
            count++;
            if (count >= maxLines) break;
        }
        return sb.ToString();
    }

    private static string GuessSeparator(string sample)
    {
        if (sample.Contains(';')) return ";";
        if (sample.Contains(',')) return ",";
        if (sample.Contains('\t')) return "\t";
        if (sample.Contains(' ')) return " ";
        return ",";
    }

    private static string[] SplitDelimitedLine(string line, string delimiter)
    {
        if (delimiter == " ")
        {
            // whitespace split (closest to pandas sep=r"\s+")
            return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (delimiter.Length == 1)
        {
            // minimal CSV-like split with quotes
            var d = delimiter[0];
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (!inQuotes && ch == d)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }
            result.Add(sb.ToString().Trim());
            return result.ToArray();
        }

        // fallback
        return line.Split(delimiter, StringSplitOptions.TrimEntries);
    }

    private static bool LooksLikeSqlQuery(string text)
    {
        var t = text.TrimStart().ToLowerInvariant();
        return t.StartsWith("select") || t.StartsWith("with") || t.StartsWith("insert") || t.StartsWith("update") ||
               t.StartsWith("delete");
    }

    private static string? GetFirstTableName(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1";
        var name = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

