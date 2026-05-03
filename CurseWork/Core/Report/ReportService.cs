using System;
using System.Collections.Generic;
using System.IO;
using CurseWork.Core.Report.Strategies;

namespace CurseWork.Core.Report
{
    public class ReportService
    {
        private readonly Dictionary<string, IReportSaveStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase)
        {
            [".docx"] = new WordReportStrategy(),
            [".xlsx"] = new ExcelReportStrategy(),
            [".txt"] = new TextReportStrategy(),
            [".csv"] = new TextReportStrategy(),
            [".db"] = new DatabaseReportStrategy(),
            [".sqlite"] = new DatabaseReportStrategy(),
            [".pdf"] = new PdfReportStrategy()
        };

        public void SaveReport(string filePath, IRegressionResult result)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь не указан", nameof(filePath));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!_strategies.TryGetValue(ext, out var strategy))
                strategy = new TextReportStrategy(); // fallback

            strategy.Save(filePath, result);
        }
    }
}