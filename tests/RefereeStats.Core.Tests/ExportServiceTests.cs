using RefereeStats.Core.Export;
using RefereeStats.Core.Models;

namespace RefereeStats.Core.Tests;

public class ExportServiceTests
{
    private static List<RefereeProfile> SampleProfiles() =>
    [
        new()
        {
            RefereeId = "12345",
            Name = "Juan Pérez",
            League = "laliga2",
            Season = "2025-2026",
            MatchesOfficiated = 18,
            YellowCards = 62,
            SecondYellowCards = 3,
            DirectRedCards = 2,
            PenaltiesAwarded = 5,
        },
    ];

    [Fact]
    public async Task ExportToCsvAsync_WritesNonEmptyFileWithHeaderAndData()
    {
        var service = new ExportService();
        var path = Path.Combine(Path.GetTempPath(), $"referees-{Guid.NewGuid()}.csv");

        try
        {
            await service.ExportToCsvAsync(SampleProfiles(), path);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.True(lines.Length >= 2);
            Assert.Contains("RefereeId", lines[0]);
            Assert.Contains("12345", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportToJsonLAsync_WritesOneJsonObjectPerLine()
    {
        var service = new ExportService();
        var path = Path.Combine(Path.GetTempPath(), $"referees-{Guid.NewGuid()}.jsonl");

        try
        {
            await service.ExportToJsonLAsync(SampleProfiles(), path);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Single(lines);
            Assert.Contains("\"RefereeId\":\"12345\"", lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportToParquetAsync_WritesValidNonEmptyFile()
    {
        var service = new ExportService();
        var path = Path.Combine(Path.GetTempPath(), $"referees-{Guid.NewGuid()}.parquet");

        try
        {
            await service.ExportToParquetAsync(SampleProfiles(), path);

            var fileInfo = new FileInfo(path);
            Assert.True(fileInfo.Exists);
            Assert.True(fileInfo.Length > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
