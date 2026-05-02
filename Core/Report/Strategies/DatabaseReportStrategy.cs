using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CurseWork.Core.Report.Strategies
{
    public sealed class DatabaseReportStrategy : IReportSaveStrategy
    {
        public void Save(string filePath, IRegressionResult result)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            var connectionString = $"Data Source={filePath};Mode=ReadWriteCreate;";
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            conn.Execute(@"
CREATE TABLE IF NOT EXISTS regression_results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at TEXT NOT NULL,
  model_type TEXT NOT NULL,
  equation TEXT NOT NULL,
  mse REAL NOT NULL,
  adj_r2 REAL NOT NULL
)");
            conn.Execute(@"
CREATE TABLE IF NOT EXISTS coefficients (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  result_id INTEGER NOT NULL,
  data TEXT NOT NULL,
  FOREIGN KEY(result_id) REFERENCES regression_results(id)
)");
            conn.Execute(@"
CREATE TABLE IF NOT EXISTS predictions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  result_id INTEGER NOT NULL,
  data TEXT NOT NULL,
  FOREIGN KEY(result_id) REFERENCES regression_results(id)
)");

            using var tx = conn.BeginTransaction();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var resultId = conn.ExecuteScalar<long>(
                @"INSERT INTO regression_results (created_at, model_type, equation, mse, adj_r2)
                  VALUES (@date, @type, @eq, @mse, @r2);
                  SELECT last_insert_rowid();",
                new
                {
                    date = now,
                    type = result.Is3D ? "3D" : "2D",
                    eq = result.Equation,
                    mse = result.Metrics.Mse,
                    r2 = result.Metrics.AdjustedR2
                }, tx);

            var coeffJson = JsonSerializer.Serialize(result.Coefficients.ToArray());
            conn.Execute("INSERT INTO coefficients (result_id, data) VALUES (@id, @json)",
                new { id = resultId, json = coeffJson }, tx);

            var predJson = JsonSerializer.Serialize(result.Predictions.ToArray());
            conn.Execute("INSERT INTO predictions (result_id, data) VALUES (@id, @json)",
                new { id = resultId, json = predJson }, tx);

            tx.Commit();
        }
    }
}