using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class WordReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Заголовок
            body.AppendChild(CreateParagraph("ОТЧЁТ О РЕГРЕССИОННОЙ МОДЕЛИ", true, 24));

            // Дата
            AddKeyValue(body, "Дата и время", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AddKeyValue(body, "Режим модели", result.Is3D ? "3D поверхность" : "2D полином");
            AddKeyValue(body, "Уравнение", result.Equation);
            AddKeyValue(body, "MSE", result.Metrics.Mse.ToString("F6"));
            AddKeyValue(body, "RMSE", Math.Sqrt(result.Metrics.Mse).ToString("F6"));
            AddKeyValue(body, "Скорректированный R²", result.Metrics.AdjustedR2.ToString("F6"));

            var img = result.GetGraphImage();
            if (img != null)
            {
                body.AppendChild(new Paragraph(new Run(new Text(result.Is3D ? "3D сцена" : "График модели"))));
                AddImageToBody(doc, body, img);
            }

            // Коэффициенты
            body.AppendChild(new Paragraph(new Run(new Text("КОЭФФИЦИЕНТЫ"))));
            var coefTable = CreateTable(new[] { "Индекс", "Значение" });
            for (int i = 0; i < result.Coefficients.Count; i++)
                AddTableRow(coefTable, i.ToString(), result.Coefficients[i].ToString("G10"));
            body.AppendChild(coefTable);
            body.AppendChild(new Paragraph());

            // Предсказания
            body.AppendChild(new Paragraph(new Run(new Text("ПРЕДСКАЗАННЫЕ ЗНАЧЕНИЯ"))));
            Table predTable;
            if (result.Is3D)
            {
                predTable = CreateTable(new[] { "X", "Y", "Z (предск.)" });
                foreach (var p in result.Predictions)
                    AddTableRow(predTable,
                        p.X.ToString(CultureInfo.InvariantCulture),
                        p.Y.ToString(CultureInfo.InvariantCulture),
                        (p.Z?.ToString(CultureInfo.InvariantCulture) ?? ""));
            }
            else
            {
                predTable = CreateTable(new[] { "X", "Y (предск.)" });
                foreach (var p in result.Predictions)
                    AddTableRow(predTable,
                        p.X.ToString(CultureInfo.InvariantCulture),
                        p.Y.ToString(CultureInfo.InvariantCulture));
            }
            body.AppendChild(predTable);

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
                    new InsideVerticalBorder() { Val = BorderValues.Single, Size = 4 }));
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
                row.AppendChild(new TableCell(new Paragraph(new Run(new Text(c ?? "")))));
            table.AppendChild(row);
        }

        private static Paragraph CreateParagraph(string text, bool bold = false, int fontSize = 12)
        {
            var run = new Run(new Text(text));
            if (bold)
                run.RunProperties = new RunProperties(new Bold());
            run.RunProperties ??= new RunProperties();
            run.RunProperties.Append(new FontSize { Val = fontSize.ToString() });
            return new Paragraph(run);
        }

        private static void AddImageToBody(WordprocessingDocument doc, Body body, byte[] imageBytes)
        {
            // Подготовка изображения
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null) return;
            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
            using (var stream = new MemoryStream(imageBytes))
                imagePart.FeedData(stream);

            var relationshipId = mainPart.GetIdOfPart(imagePart);
            var imageElement = CreateImageElement(relationshipId, imageBytes.Length);
            var paragraph = new Paragraph();
            var run = new Run(imageElement);
            paragraph.AppendChild(run);
            body.AppendChild(paragraph);
        }

        private static Drawing CreateImageElement(string relationshipId, long imageSizeBytes)
        {
            // Размеры в EMU (1 дюйм = 914400 EMU, 800px @96dpi => 800/96*914400 = 7 620 000)
            const double emuPerInch = 914400;
            double dpi = 96;
            // Предполагаем, что ширина 800px, можно извлечь из imageBytes, но для примера зафиксируем 800
            int widthPx = 800;
            int heightPx = 500;
            long cx = (long)(widthPx / dpi * emuPerInch);
            long cy = (long)(heightPx / dpi * emuPerInch);

            var element = new Drawing(
                new DW.Inline(
                    new DW.Extent() { Cx = cx, Cy = cy },
                    new DW.EffectExtent() { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                    new DW.DocProperties() { Id = 1, Name = "Picture 1" },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks() { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties() { Id = 0, Name = "graph.png" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip() { Embed = relationshipId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset() { X = 0, Y = 0 },
                                        new A.Extents() { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = 0,
                    DistanceFromBottom = 0,
                    DistanceFromLeft = 0,
                    DistanceFromRight = 0
                }
            );
            return element;
        }
    }
}