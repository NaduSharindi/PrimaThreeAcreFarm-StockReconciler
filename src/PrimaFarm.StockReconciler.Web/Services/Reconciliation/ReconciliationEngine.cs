using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Services.Reconciliation;

public sealed record ResultLine(
    string Category,
    string ItemCode,
    string ItemName,
    decimal? ErpQty,
    decimal? FarmQty,
    decimal Difference,
    int FarmLineCount,
    string SourceRows);

public sealed class ReconciliationResult
{
    public List<ResultLine> Lines { get; } = [];
    public int Count(string category) => Lines.Count(l => l.Category == category);
    public int SummedLineCount => Lines.Count(l => l.FarmLineCount > 1);
    public decimal TotalErpQty => Lines.Sum(l => l.ErpQty ?? 0);
    public decimal TotalFarmQty => Lines.Sum(l => l.FarmQty ?? 0);
}

/// <summary>
/// Pure comparison logic (no I/O): aggregates lines by item code, then classifies each code as
/// Matched, Mismatched, MissingInFarm (in ERP only) or NotInErp (in farm file only).
/// </summary>
public static class ReconciliationEngine
{
    private const int MaxSourceRefs = 20;

    private sealed class Aggregate
    {
        public string Name = "";
        public decimal Qty;
        public int LineCount;
        public List<string> Refs = [];
    }

    public static ReconciliationResult Compare(
        IEnumerable<StockLine> erpLines,
        IEnumerable<StockLine> farmLines,
        decimal tolerance = 0m,
        bool includeZeroErpMissing = true)
    {
        var erp = AggregateByItem(erpLines);
        var farm = AggregateByItem(farmLines);
        var result = new ReconciliationResult();

        foreach (var code in erp.Keys.Union(farm.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            erp.TryGetValue(code, out var e);
            farm.TryGetValue(code, out var f);

            if (e is not null && f is not null)
            {
                var diff = f.Qty - e.Qty;
                var category = Math.Abs(diff) <= tolerance ? LineCategories.Matched : LineCategories.Mismatched;
                result.Lines.Add(new ResultLine(category, code, Pick(e.Name, f.Name), e.Qty, f.Qty, diff, f.LineCount, Refs(f)));
            }
            else if (e is not null)
            {
                if (!includeZeroErpMissing && e.Qty == 0) continue;
                result.Lines.Add(new ResultLine(LineCategories.MissingInFarm, code, e.Name, e.Qty, null, -e.Qty, 0, Refs(e)));
            }
            else
            {
                result.Lines.Add(new ResultLine(LineCategories.NotInErp, code, f!.Name, null, f.Qty, f.Qty, f.LineCount, Refs(f)));
            }
        }
        return result;
    }

    private static Dictionary<string, Aggregate> AggregateByItem(IEnumerable<StockLine> lines)
    {
        var map = new Dictionary<string, Aggregate>(StringComparer.OrdinalIgnoreCase);
        foreach (var ln in lines)
        {
            if (!map.TryGetValue(ln.ItemCode, out var a)) map[ln.ItemCode] = a = new Aggregate();
            a.Qty += ln.Qty;
            a.LineCount++;
            if (a.Name.Length == 0) a.Name = ln.ItemName;
            if (a.Refs.Count < MaxSourceRefs) a.Refs.Add($"{ln.SourceSheet}!R{ln.SourceRow}");
        }
        return map;
    }

    private static string Pick(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;

    private static string Refs(Aggregate a) =>
        string.Join(", ", a.Refs) + (a.LineCount > a.Refs.Count ? $" (+{a.LineCount - a.Refs.Count} more)" : "");
}
