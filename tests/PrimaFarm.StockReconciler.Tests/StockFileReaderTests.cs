using System.Text;
using ClosedXML.Excel;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Reconciliation;

namespace PrimaFarm.StockReconciler.Tests;

public class StockFileReaderTests
{
    private static string SamplesDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "samples"));

    private static IReadOnlyList<StockLine> ReadSample(string name, StockFileOptions? options = null)
    {
        using var fs = File.OpenRead(Path.Combine(SamplesDir(), name));
        return StockFileReader.Read(fs, name, options ?? StockFileOptions.ForErp());
    }

    private static IReadOnlyList<StockLine> ReadText(string text, string name) =>
        StockFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), name, StockFileOptions.ForErp());

    [Theory]
    [InlineData("On-hand", 30)]
    [InlineData("C/B", 45)]
    [InlineData("Balance 25.07.2026", 25)]
    [InlineData("25.07.2026", 20)]
    [InlineData("farm qty (26.06.2026)", 0)]
    [InlineData("O/B", 0)]
    [InlineData("Allocatble", 0)]
    [InlineData("Recived", 0)]
    public void Scores_quantity_headers(string header, int expected) =>
        Assert.Equal(expected, StockFileReader.ScoreQtyHeader(header.ToLowerInvariant()));

    [Theory]
    [InlineData("EMHKP 0080", "EMHKP0080")]
    [InlineData(" eatb0001 ", "EATB0001")]
    [InlineData("TOTAL", "")]
    [InlineData("Prepared by: Farm Manager", "")]
    [InlineData("Item Code", "")]
    public void Cleans_item_codes(string raw, string expected) =>
        Assert.Equal(expected, StockFileReader.CleanCode(raw));

    [Fact]
    public void Reads_erp_csv_and_picks_on_hand_not_allocated()
    {
        var lines = ReadSample("erp_kosgama_616.csv");
        Assert.Equal(11, lines.Count);
        Assert.Equal(5m, lines.Where(l => l.ItemCode == "EATB0001").Sum(l => l.Qty));
    }

    [Fact]
    public void Reads_farm_workbook_across_sheets_and_skips_excluded_sheet()
    {
        var kosgama = new Location { FarmSheetMode = FarmSheetModes.AllSheets, ExcludedSheets = "Sheet1" };
        var lines = ReadSample("farm_kosgama_616.xlsx", StockFileOptions.ForFarm(kosgama));

        Assert.DoesNotContain(lines, l => l.ItemCode == "ZZZZ9999");                  // excluded sheet
        Assert.Equal(15m, lines.Single(l => l.ItemCode == "EASA0050").Qty);            // date column, not "Farm Qty"
        Assert.Equal(16m, lines.Single(l => l.ItemCode == "GSATCL062").Qty);           // C/B on STATIONERY sheet
        Assert.Equal(2, lines.Count(l => l.ItemCode == "EMHKP0080"));                  // "EMHKP 0080" normalised
        Assert.DoesNotContain(lines, l => l.ItemCode.Contains("PREPARED"));
    }

    [Fact]
    public void Last_sheet_mode_reads_only_the_latest_month()
    {
        using var wb = new XLWorkbook();
        foreach (var (month, qty) in new[] { ("JANUARY", 5), ("FEBRUARY", 9) })
        {
            var ws = wb.Worksheets.Add(month);
            ws.Cell(1, 1).Value = "Item Code"; ws.Cell(1, 2).Value = "Balance";
            ws.Cell(2, 1).Value = "EASA0050"; ws.Cell(2, 2).Value = qty;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var lines = StockFileReader.Read(ms, "m.xlsx", new StockFileOptions { FarmSheetMode = FarmSheetModes.LastSheet });

        Assert.Equal(9m, Assert.Single(lines).Qty);
    }

    [Fact]
    public void Reads_side_by_side_tables()
    {
        var csv = "Item Code,ITEM,Balance,,Item Code,ITEM,Balance\nEASA0050,Spark plug,5,,GUOT0003,Blue cap,3\n";
        var lines = ReadText(csv, "x.csv");
        Assert.Equal(["EASA0050", "GUOT0003"], lines.Select(l => l.ItemCode).Order());
    }

    [Fact]
    public void Reads_as400_extract_and_validates_control_totals()
    {
        var lines = ReadSample("erp_kosgama_616_as400.txt");
        Assert.Equal(11, lines.Count);
        Assert.Equal(65m, lines.Sum(l => l.Qty));
        Assert.All(lines, l => Assert.Equal("AS400", l.SourceSheet));
    }

    [Fact]
    public void Rejects_as400_extract_with_wrong_trailer_count()
    {
        var text = File.ReadAllText(Path.Combine(SamplesDir(), "erp_kosgama_616_as400.txt")).Replace("T000000011", "T000000012");
        var ex = Assert.Throws<StockFileFormatException>(() => ReadText(text, "bad.txt"));
        Assert.Contains("control total", ex.Message);
    }

    [Fact]
    public void Rejects_truncated_as400_extract()
    {
        var text = string.Join("\n", File.ReadAllLines(Path.Combine(SamplesDir(), "erp_kosgama_616_as400.txt")).SkipLast(1));
        Assert.Throws<StockFileFormatException>(() => ReadText(text, "cut.txt"));
    }

    [Fact]
    public void Rejects_corrupt_workbook_with_friendly_error()
    {
        var ex = Assert.Throws<StockFileFormatException>(() =>
            StockFileReader.Read(new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2, 3]), "x.xlsx", StockFileOptions.ForErp()));
        Assert.Contains("could not be opened", ex.Message);
    }

    /// <summary>
    /// Optional smoke test against real farm workbooks, which never live in this repository.
    /// Set PRIMA_REAL_SAMPLES_DIR to a folder containing Book1-616.xlsx and a Kosgama farm workbook.
    /// </summary>
    [Fact]
    public void Real_kosgama_files_smoke_test()
    {
        var dir = Environment.GetEnvironmentVariable("PRIMA_REAL_SAMPLES_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var erpPath = Path.Combine(dir, "Book1-616.xlsx");
        var farmPath = Directory.GetFiles(dir, "*Non Product*.xlsx").FirstOrDefault();
        if (!File.Exists(erpPath) || farmPath is null) return;

        using var e = File.OpenRead(erpPath);
        using var f = File.OpenRead(farmPath);
        var erp = StockFileReader.Read(e, erpPath, StockFileOptions.ForErp());
        var farm = StockFileReader.Read(f, farmPath, StockFileOptions.ForFarm(new Location { ExcludedSheets = "Sheet1" }));
        var result = ReconciliationEngine.Compare(erp, farm);

        Assert.True(erp.Count > 1000, $"ERP lines: {erp.Count}");
        Assert.True(farm.Count > 100, $"Farm lines: {farm.Count}");
        Assert.True(result.Count(LineCategories.Matched) > 0);
    }
}
