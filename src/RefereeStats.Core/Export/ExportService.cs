using System.Globalization;
using CsvHelper;
using Parquet;
using Parquet.Serialization;

namespace RefereeStats.Core.Export;

/// <summary>Exporta colecciones de datos a CSV, Parquet y JSON Lines (newline-delimited JSON).</summary>
public sealed class ExportService
{
    public async Task ExportToCsvAsync<T>(IEnumerable<T> records, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var writer = new StreamWriter(path);
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(records, ct);
    }

    public async Task ExportToParquetAsync<T>(IEnumerable<T> records, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var list = records as IList<T> ?? records.ToList();
        await ParquetSerializer.SerializeAsync(list, path, cancellationToken: ct);
    }

    public async Task ExportToJsonLAsync<T>(IEnumerable<T> records, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var writer = new StreamWriter(path);
        foreach (var record in records)
        {
            var line = System.Text.Json.JsonSerializer.Serialize(record);
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }
}
