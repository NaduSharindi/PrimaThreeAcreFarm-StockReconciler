namespace PrimaFarm.StockReconciler.Web.Models;

// Entities map 1:1 to the tables created by database/001_schema.sql.

public class Location
{
    public int LocationId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ErpLocationNo { get; set; }
    public bool IsEnabled { get; set; }
    public string Status { get; set; } = "Ready";
    public string? Notes { get; set; }
    public string FarmSheetMode { get; set; } = FarmSheetModes.AllSheets;
    public string? ExcludedSheets { get; set; }
    public decimal Tolerance { get; set; }
    public bool IncludeZeroErpMissing { get; set; } = true;
}

public static class FarmSheetModes
{
    public const string AllSheets = "AllSheets";
    public const string LastSheet = "LastSheet";
}

public class AppUser
{
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Officer;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Officer = "Officer";
    public const string Auditor = "Auditor";
}

public class ReconciliationRun
{
    public int RunId { get; set; }
    public string RunReference { get; set; } = "";
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public string ErpSource { get; set; } = ErpSources.Upload;
    public string? ErpFileName { get; set; }
    public string? FarmFileNames { get; set; }
    public string Status { get; set; } = RunStatuses.Completed;
    public int MatchedCount { get; set; }
    public int MismatchedCount { get; set; }
    public int MissingInFarmCount { get; set; }
    public int NotInErpCount { get; set; }
    public int SummedLineCount { get; set; }
    public decimal TotalErpQty { get; set; }
    public decimal TotalFarmQty { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string? ReviewComment { get; set; }
    public string? FinanceReference { get; set; }
    public byte[] RowVer { get; set; } = [];
    public List<ReconciliationLine> Lines { get; set; } = [];
}

public static class ErpSources
{
    public const string Upload = "Upload";
    public const string Api = "Api";
    public const string AS400 = "AS400";
}

public static class RunStatuses
{
    public const string Completed = "Completed";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Posted = "Posted";
    public const string PostingFailed = "PostingFailed";
}

public class ReconciliationLine
{
    public long LineId { get; set; }
    public int RunId { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string Category { get; set; } = LineCategories.Matched;
    public decimal? ErpQty { get; set; }
    public decimal? FarmQty { get; set; }
    public decimal Difference { get; set; }
    public int FarmLineCount { get; set; }
    public string? SourceRows { get; set; }
}

public static class LineCategories
{
    public const string Matched = "Matched";
    public const string Mismatched = "Mismatched";
    public const string MissingInFarm = "MissingInFarm";
    public const string NotInErp = "NotInErp";
}

public class AuditLog
{
    public long AuditId { get; set; }
    public DateTime EventUtc { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Entity { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}

public class IntegrationLog
{
    public long IntegrationLogId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string TargetSystem { get; set; } = "";
    public string Direction { get; set; } = "Outbound";
    public string Operation { get; set; } = "";
    public int? RunId { get; set; }
    public string? IdempotencyKey { get; set; }
    public int? HttpStatus { get; set; }
    public bool Succeeded { get; set; }
    public int DurationMs { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
}

public class MockFinanceJournal
{
    public int JournalId { get; set; }
    public string JournalNo { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public int LineCount { get; set; }
    public decimal NetVarianceQty { get; set; }
    public string? Narrative { get; set; }
    public DateTime ReceivedUtc { get; set; }
}

// Keyless result sets returned by stored procedures (database/002_stored_procedures.sql).

public class RunSearchRow
{
    public int RunId { get; set; }
    public string RunReference { get; set; } = "";
    public string LocationCode { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string ErpSource { get; set; } = "";
    public string Status { get; set; } = "";
    public int MatchedCount { get; set; }
    public int MismatchedCount { get; set; }
    public int MissingInFarmCount { get; set; }
    public int NotInErpCount { get; set; }
    public decimal AccuracyPct { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public string? FinanceReference { get; set; }
    public int TotalCount { get; set; }
}

public class DashboardRow
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = "";
    public bool IsEnabled { get; set; }
    public int RunCount { get; set; }
    public int MismatchedLines { get; set; }
    public int MissingLines { get; set; }
    public int PendingReview { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public decimal AvgAccuracyPct { get; set; }
}

public class VarianceRow
{
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string Category { get; set; } = "";
    public decimal? ErpQty { get; set; }
    public decimal? FarmQty { get; set; }
    public decimal Difference { get; set; }
    public decimal AbsDifference { get; set; }
}
