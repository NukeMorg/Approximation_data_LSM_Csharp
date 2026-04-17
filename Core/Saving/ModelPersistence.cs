using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CurseWork.Core.Saving;

/// <summary>
/// Сохранение и загрузка полной модели регрессии в JSON.
/// </summary>
public static class ModelPersistence
{
    public static void SaveModel(
        string filePath,
        double[] coefficients,
        double mse,
        double r2Adjusted,
        double[] yPred,
        double[]? xValues = null,
        string sourceFile = "")
    {
        var model = new
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            SourceFile = sourceFile,
            Coefficients = coefficients,
            MSE = mse,
            RMSE = Math.Sqrt(mse),
            R2Adjusted = r2Adjusted,
            X = xValues,
            Predictions = yPred
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        string json = JsonSerializer.Serialize(model, options);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public static LoadedModel LoadModel(string filePath)
    {
        var json = File.ReadAllText(filePath, Encoding.UTF8);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var coefficients = root.GetProperty("Coefficients").EnumerateArray()
            .Select(e => e.GetDouble()).ToArray();

        var mse = root.GetProperty("MSE").GetDouble();
        var r2Adjusted = root.GetProperty("R2Adjusted").GetDouble();
        var yPred = root.GetProperty("Predictions").EnumerateArray()
            .Select(e => e.GetDouble()).ToArray();

        double[]? xValues = null;
        if (root.TryGetProperty("X", out var xProp) && xProp.ValueKind != JsonValueKind.Null)
        {
            xValues = xProp.EnumerateArray().Select(e => e.GetDouble()).ToArray();
        }

        string sourceFile = "";
        if (root.TryGetProperty("SourceFile", out var srcProp))
            sourceFile = srcProp.GetString() ?? "";

        return new LoadedModel
        {
            Coefficients = coefficients,
            MSE = mse,
            R2Adjusted = r2Adjusted,
            Predictions = yPred,
            X = xValues,
            SourceFile = sourceFile
        };
    }
}

public class LoadedModel
{
    public double[] Coefficients { get; set; } = Array.Empty<double>();
    public double MSE { get; set; }
    public double R2Adjusted { get; set; }
    public double[] Predictions { get; set; } = Array.Empty<double>();
    public double[]? X { get; set; }
    public string SourceFile { get; set; } = "";
}