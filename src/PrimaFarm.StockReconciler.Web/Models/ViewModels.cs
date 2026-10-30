using System.ComponentModel.DataAnnotations;

namespace PrimaFarm.StockReconciler.Web.Models;

public class LoginViewModel
{
    [Required, StringLength(50)]
    public string Username { get; set; } = "";

    [Required, StringLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class DashboardViewModel
{
    public List<DashboardRow> Rows { get; set; } = [];
    public List<RunSearchRow> PendingReview { get; set; } = [];
    public int Days { get; set; }
}

public class ReconcileViewModel
{
    public List<Location> Locations { get; set; } = [];
    public int? SelectedLocationId { get; set; }
    public string? Error { get; set; }
}

public class RunsIndexViewModel
{
    public List<RunSearchRow> Rows { get; set; } = [];
    public List<Location> Locations { get; set; } = [];
    public int? LocationId { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public class RunDetailsViewModel
{
    public ReconciliationRun Run { get; set; } = null!;
    public List<VarianceRow> TopVariances { get; set; } = [];
    public bool CanReview { get; set; }
    public bool CanRetryPosting { get; set; }
}
