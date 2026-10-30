using System.ComponentModel.DataAnnotations;

namespace PrimaFarm.StockReconciler.Web.Models;

// Public REST contracts (v1). Classes with parameterless constructors so both the JSON and
// XML formatters can serialise them; request bodies may be sent as application/json or application/xml.

public class ApiStockLine
{
    [Required, StringLength(50, MinimumLength = 1)]
    public string ItemCode { get; set; } = "";

    [StringLength(200)]
    public string? ItemName { get; set; }

    [Range(-1_000_000_000, 1_000_000_000)]
    public decimal Qty { get; set; }
}

public class ReconcileApiRequest
{
    [Required, StringLength(30)]
    public string LocationCode { get; set; } = "";

    [Required, MinLength(1), MaxLength(50_000)]
    public List<ApiStockLine> ErpLines { get; set; } = [];

    [Required, MinLength(1), MaxLength(50_000)]
    public List<ApiStockLine> FarmLines { get; set; } = [];
}

public class LocationDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ErpLocationNo { get; set; }
    public bool Enabled { get; set; }
    public string Status { get; set; } = "";
}

public class RunSummaryDto
{
    public string RunReference { get; set; } = "";
    public string Location { get; set; } = "";
    public string ErpSource { get; set; } = "";
    public string Status { get; set; } = "";
    public int Matched { get; set; }
    public int Mismatched { get; set; }
    public int MissingInFarm { get; set; }
    public int NotInErp { get; set; }
    public decimal AccuracyPct { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string? FinanceReference { get; set; }
}

public class VarianceDto
{
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string Category { get; set; } = "";
    public decimal? ErpQty { get; set; }
    public decimal? FarmQty { get; set; }
    public decimal Difference { get; set; }
}

public class PagedResult<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = [];
}
