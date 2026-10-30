using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Reconciliation;

namespace PrimaFarm.StockReconciler.Tests;

public class ReconciliationEngineTests
{
    private static StockLine L(string code, decimal qty, string sheet = "S", int row = 1) => new(code, code + " name", qty, "f.xlsx", sheet, row);

    [Fact]
    public void Classifies_every_category()
    {
        var erp = new[] { L("A1", 10), L("B2", 5), L("C3", 7) };
        var farm = new[] { L("A1", 10), L("B2", 4), L("D4", 2) };

        var result = ReconciliationEngine.Compare(erp, farm);

        Assert.Equal(1, result.Count(LineCategories.Matched));
        Assert.Equal(1, result.Count(LineCategories.Mismatched));
        Assert.Equal(1, result.Count(LineCategories.MissingInFarm));
        Assert.Equal(1, result.Count(LineCategories.NotInErp));
        Assert.Equal(-1m, result.Lines.Single(l => l.ItemCode == "B2").Difference);
        Assert.Equal(-7m, result.Lines.Single(l => l.ItemCode == "C3").Difference);
        Assert.Equal(2m, result.Lines.Single(l => l.ItemCode == "D4").Difference);
    }

    [Fact]
    public void Sums_repeated_lines_on_both_sides()
    {
        var erp = new[] { L("A1", 1), L("A1", 2), L("A1", 2) };           // ERP: one line per lot
        var farm = new[] { L("A1", 2, "Shed A", 4), L("A1", 3, "Shed B", 9) };

        var line = Assert.Single(ReconciliationEngine.Compare(erp, farm).Lines);

        Assert.Equal(LineCategories.Matched, line.Category);
        Assert.Equal(5m, line.ErpQty);
        Assert.Equal(2, line.FarmLineCount);
        Assert.Contains("Shed B!R9", line.SourceRows);
    }

    [Fact]
    public void Item_codes_match_case_insensitively()
    {
        var result = ReconciliationEngine.Compare([L("eatb0001", 3)], [L("EATB0001", 3)]);
        Assert.Equal(LineCategories.Matched, Assert.Single(result.Lines).Category);
    }

    [Theory]
    [InlineData(0.5, LineCategories.Matched)]
    [InlineData(0.0, LineCategories.Mismatched)]
    public void Applies_location_tolerance(double tolerance, string expected)
    {
        var result = ReconciliationEngine.Compare([L("A1", 10m)], [L("A1", 10.4m)], (decimal)tolerance);
        Assert.Equal(expected, Assert.Single(result.Lines).Category);
    }

    [Fact]
    public void Can_ignore_zero_balance_erp_items_missing_from_farm()
    {
        var erp = new[] { L("A1", 0), L("B2", 3) };
        var result = ReconciliationEngine.Compare(erp, [], includeZeroErpMissing: false);
        Assert.Equal("B2", Assert.Single(result.Lines).ItemCode);
    }
}
