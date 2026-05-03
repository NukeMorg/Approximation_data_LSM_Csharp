using System;
using System.Globalization;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class PdfReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            using var fs = new FileStream(filePath, FileMode.Create);
            var doc = new Document(PageSize.A4, 36, 36, 50, 36);
            var writer = PdfWriter.GetInstance(doc, fs);
            doc.Open();

            // Единственный шрифт — Arial (должен быть установлен в Windows)
            string fontPath = @"C:\Windows\Fonts\arial.ttf";
            if (!File.Exists(fontPath))
                throw new Exception("Шрифт Arial не найден по пути " + fontPath);

            var baseFont = BaseFont.CreateFont(fontPath, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);

            var titleFont = new Font(baseFont, 18, Font.BOLD);
            var headingFont = new Font(baseFont, 14, Font.BOLD);
            var normalFont = new Font(baseFont, 12, Font.NORMAL);
            var boldFont = new Font(baseFont, 12, Font.BOLD);

            // 1. Заголовок
            doc.Add(new Paragraph("ОТЧЁТ О РЕГРЕССИОННОЙ МОДЕЛИ", titleFont));
            doc.Add(new Paragraph($"Дата и время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", normalFont));
            doc.Add(new Paragraph($"Режим модели: {(result.Is3D ? "3D поверхность" : "2D полином")}", normalFont));
            doc.Add(new Paragraph($"Уравнение: {result.Equation}", normalFont));
            doc.Add(new Paragraph($"MSE: {result.Metrics.Mse:F6}", normalFont));
            doc.Add(new Paragraph($"RMSE: {Math.Sqrt(result.Metrics.Mse):F6}", normalFont));
            doc.Add(new Paragraph($"Скорректированный R²: {result.Metrics.AdjustedR2:F6}", normalFont));
            doc.Add(new Paragraph(" "));

            // 2. Изображение (если есть)
            var imgBytes = result.GetGraphImage();
            if (imgBytes != null && imgBytes.Length > 0)
            {
                string caption = result.Is3D ? "3D сцена" : "График модели";
                doc.Add(new Paragraph(caption, headingFont));
                var img = Image.GetInstance(imgBytes);
                img.ScaleToFit(500f, 350f);
                img.Alignment = Image.ALIGN_CENTER;
                doc.Add(img);
                doc.Add(new Paragraph(" "));
            }

            // 3. Таблица коэффициентов
            doc.Add(new Paragraph("Коэффициенты", headingFont));
            var coefTable = new PdfPTable(2) { WidthPercentage = 100 };
            coefTable.SetWidths(new float[] { 30f, 70f });
            coefTable.AddCell(new Phrase("Индекс", boldFont));
            coefTable.AddCell(new Phrase("Значение", boldFont));
            for (int i = 0; i < result.Coefficients.Count; i++)
            {
                coefTable.AddCell(new Phrase(i.ToString(), normalFont));
                coefTable.AddCell(new Phrase(result.Coefficients[i].ToString("G10"), normalFont));
            }
            doc.Add(coefTable);
            doc.Add(new Paragraph(" "));

            // 4. Таблица предсказанных значений
            doc.Add(new Paragraph("Предсказанные значения", headingFont));
            PdfPTable predTable;
            if (result.Is3D)
            {
                predTable = new PdfPTable(3) { WidthPercentage = 100 };
                predTable.SetWidths(new float[] { 33f, 33f, 34f });
                predTable.AddCell(new Phrase("X", boldFont));
                predTable.AddCell(new Phrase("Y", boldFont));
                predTable.AddCell(new Phrase("Z (предск.)", boldFont));
                foreach (var p in result.Predictions)
                {
                    predTable.AddCell(new Phrase(p.X.ToString(CultureInfo.InvariantCulture), normalFont));
                    predTable.AddCell(new Phrase(p.Y.ToString(CultureInfo.InvariantCulture), normalFont));
                    predTable.AddCell(new Phrase(p.Z?.ToString(CultureInfo.InvariantCulture) ?? "", normalFont));
                }
            }
            else
            {
                predTable = new PdfPTable(2) { WidthPercentage = 100 };
                predTable.SetWidths(new float[] { 40f, 60f });
                predTable.AddCell(new Phrase("X", boldFont));
                predTable.AddCell(new Phrase("Y (предск.)", boldFont));
                foreach (var p in result.Predictions)
                {
                    predTable.AddCell(new Phrase(p.X.ToString(CultureInfo.InvariantCulture), normalFont));
                    predTable.AddCell(new Phrase(p.Y.ToString(CultureInfo.InvariantCulture), normalFont));
                }
            }
            doc.Add(predTable);

            doc.Close();
        }
    }
}