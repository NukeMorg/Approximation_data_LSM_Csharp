using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

            try
            {
                using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);

                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                // Лист 1: Результаты
                var infoSheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var infoSheet = new Worksheet();
                infoSheetPart.Worksheet = infoSheet;

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(infoSheetPart),
                    SheetId = 1,
                    Name = "Результаты"
                });

                var infoData = new SheetData();
                // Строка 1 (заголовки)
                var row1 = new Row { RowIndex = 1 };
                AddBoldInlineStringCell(row1, "A", "Параметр");
                AddBoldInlineStringCell(row1, "B", "Значение");
                infoData.Append(row1);

                // Последующие строки
                int rowNum = 2;
                AddInfoRow(infoData, "Дата и время", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), rowNum++);
                AddInfoRow(infoData, "Уравнение", result.Equation ?? "", rowNum++);
                AddInfoRow(infoData, "MSE", result.Metrics.Mse.ToString("F6"), rowNum++);
                AddInfoRow(infoData, "RMSE", Math.Sqrt(result.Metrics.Mse).ToString("F6"), rowNum++);
                AddInfoRow(infoData, "Скорректированный R²", result.Metrics.AdjustedR2.ToString("F6"), rowNum++);

                infoSheet.Append(infoData);
                AutoFitColumns(infoSheet, new[] { 1u, 2u });
                infoSheetPart.Worksheet.Save();

                // Лист 2: Коэффициенты
                var coefSheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var coefSheet = new Worksheet();
                coefSheetPart.Worksheet = coefSheet;

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(coefSheetPart),
                    SheetId = 2,
                    Name = "Коэффициенты"
                });

                var coefData = new SheetData();
                var coefRow1 = new Row { RowIndex = 1 };
                AddBoldInlineStringCell(coefRow1, "A", "Индекс");
                AddBoldInlineStringCell(coefRow1, "B", "Значение");
                coefData.Append(coefRow1);

                for (int i = 0; i < result.Coefficients.Count; i++)
                {
                    var r = new Row { RowIndex = (uint)(i + 2) };
                    AddInlineStringCell(r, "A", i.ToString());
                    AddNumericCell(r, "B", result.Coefficients[i]);
                    coefData.Append(r);
                }

                coefSheet.Append(coefData);
                AutoFitColumns(coefSheet, new[] { 1u, 2u });
                coefSheetPart.Worksheet.Save();

                // Лист 3: Предсказания
                var predSheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var predSheet = new Worksheet();
                predSheetPart.Worksheet = predSheet;

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(predSheetPart),
                    SheetId = 3,
                    Name = "Предсказания"
                });

                var predData = new SheetData();
                int count = result.Predictions.Count;

                if (result.Is3D)
                {
                    var predRow1 = new Row { RowIndex = 1 };
                    AddBoldInlineStringCell(predRow1, "A", "X");
                    AddBoldInlineStringCell(predRow1, "B", "Y");
                    AddBoldInlineStringCell(predRow1, "C", "Z (предск.)");
                    predData.Append(predRow1);

                    for (int i = 0; i < count; i++)
                    {
                        var p = result.Predictions[i];
                        var r = new Row { RowIndex = (uint)(i + 2) };
                        AddNumericCell(r, "A", p.X);
                        AddNumericCell(r, "B", p.Y);
                        AddNumericCell(r, "C", p.Z ?? 0.0);
                        predData.Append(r);
                    }
                    AutoFitColumns(predSheet, new[] { 1u, 2u, 3u });
                }
                else
                {
                    var predRow1 = new Row { RowIndex = 1 };
                    AddBoldInlineStringCell(predRow1, "A", "X");
                    AddBoldInlineStringCell(predRow1, "B", "Y (предск.)");
                    predData.Append(predRow1);

                    for (int i = 0; i < count; i++)
                    {
                        var p = result.Predictions[i];
                        var r = new Row { RowIndex = (uint)(i + 2) };
                        AddNumericCell(r, "A", p.X);
                        AddNumericCell(r, "B", p.Y);
                        predData.Append(r);
                    }
                    AutoFitColumns(predSheet, new[] { 1u, 2u });
                }

                predSheet.Append(predData);
                predSheetPart.Worksheet.Save();

                workbookPart.Workbook.Save();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Ошибка при формировании Excel-файла: {ex.Message}", ex);
            }
        }

        // ---------- Стили ----------
        private Stylesheet CreateStylesheet()
        {
            var fonts = new Fonts(
                new Font(),                                       // индекс 0
                new Font(new Bold())                              // индекс 1
            );
            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
            );
            var borders = new Borders(new Border());
            var cellStyleFormats = new CellStyleFormats(new CellFormat());
            var cellFormats = new CellFormats(
                new CellFormat(),                                 // 0 – обычный
                new CellFormat { FontId = 1, ApplyFont = true }   // 1 – жирный
            );
            return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats);
        }

        // ---------- Хелперы (теперь принимают Row) ----------
        private void AddBoldInlineStringCell(Row row, string colLetter, string text)
        {
            row.Append(new Cell
            {
                CellReference = $"{colLetter}{row.RowIndex}",
                DataType = CellValues.InlineString,
                StyleIndex = 1,
                InlineString = new InlineString(
                    new Run(
                        new RunProperties(new Bold()),
                        new Text { Text = text, Space = SpaceProcessingModeValues.Preserve }
                    )
                )
            });
        }

        private void AddInlineStringCell(Row row, string colLetter, string text)
        {
            row.Append(new Cell
            {
                CellReference = $"{colLetter}{row.RowIndex}",
                DataType = CellValues.InlineString,
                StyleIndex = 0,
                InlineString = new InlineString(
                    new Text { Text = text, Space = SpaceProcessingModeValues.Preserve }
                )
            });
        }

        private void AddNumericCell(Row row, string colLetter, double value)
        {
            row.Append(new Cell
            {
                CellReference = $"{colLetter}{row.RowIndex}",
                DataType = CellValues.Number,
                CellValue = new CellValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            });
        }

        private void AddInfoRow(SheetData sheetData, string param, string value, int rowNum)
        {
            var row = new Row { RowIndex = (uint)rowNum };
            AddInlineStringCell(row, "A", param);
            AddInlineStringCell(row, "B", value);
            sheetData.Append(row);
        }

        private void AutoFitColumns(Worksheet worksheet, uint[] columnIndices)
        {
            var columns = new Columns();
            foreach (uint col in columnIndices)
            {
                columns.Append(new Column
                {
                    Min = col,
                    Max = col,
                    Width = 20,
                    CustomWidth = true
                });
            }

            var sheetData = worksheet.GetFirstChild<SheetData>();
            if (sheetData != null)
                worksheet.InsertBefore(columns, sheetData);
            else
                worksheet.InsertAt(columns, 0);
        }
    }
}