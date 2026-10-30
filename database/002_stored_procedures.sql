/* =============================================================================
   Prima Three Acre Farms - Stock Reconciler
   002_stored_procedures.sql : views and stored procedures used by the application
   All procedures are CREATE OR ALTER (SQL Server 2016 SP1+) and fully parameterised.
   ============================================================================= */
SET NOCOUNT ON;
GO

/* ---------- View: run list with location name ---------- */
CREATE OR ALTER VIEW dbo.vw_RunSummary
AS
SELECT  r.RunId,
        r.RunReference,
        r.LocationId,
        l.Code        AS LocationCode,
        l.Name        AS LocationName,
        r.ErpSource,
        r.Status,
        r.MatchedCount,
        r.MismatchedCount,
        r.MissingInFarmCount,
        r.NotInErpCount,
        r.SummedLineCount,
        r.TotalErpQty,
        r.TotalFarmQty,
        r.CreatedBy,
        r.CreatedUtc,
        r.ReviewedBy,
        r.ReviewedUtc,
        r.FinanceReference,
        CAST(CASE WHEN (r.MatchedCount + r.MismatchedCount) = 0 THEN 0
                  ELSE 100.0 * r.MatchedCount / (r.MatchedCount + r.MismatchedCount) END AS DECIMAL(5,1)) AS AccuracyPct
FROM    dbo.ReconciliationRuns r
JOIN    dbo.Locations l ON l.LocationId = r.LocationId;
GO

/* ---------- Next run reference: REC-yyyyMMdd-000001 ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetNextRunReference
    @RunReference NVARCHAR(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n INT = NEXT VALUE FOR dbo.RunReferenceSeq;
    SET @RunReference = N'REC-' + CONVERT(NCHAR(8), SYSUTCDATETIME(), 112) + N'-' + RIGHT(N'000000' + CAST(@n AS NVARCHAR(10)), 6);
END
GO

/* ---------- Paged + filtered run search ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_SearchRuns
    @LocationId INT          = NULL,
    @Status     NVARCHAR(20) = NULL,
    @FromUtc    DATETIME2(0) = NULL,
    @ToUtc      DATETIME2(0) = NULL,
    @PageNumber INT          = 1,
    @PageSize   INT          = 20
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize NOT BETWEEN 1 AND 200 SET @PageSize = 20;

    SELECT  v.RunId, v.RunReference, v.LocationCode, v.LocationName, v.ErpSource, v.Status,
            v.MatchedCount, v.MismatchedCount, v.MissingInFarmCount, v.NotInErpCount,
            v.AccuracyPct, v.CreatedBy, v.CreatedUtc, v.ReviewedBy, v.FinanceReference,
            COUNT(*) OVER () AS TotalCount
    FROM    dbo.vw_RunSummary v
    WHERE   (@LocationId IS NULL OR v.LocationId = @LocationId)
      AND   (@Status     IS NULL OR v.Status     = @Status)
      AND   (@FromUtc    IS NULL OR v.CreatedUtc >= @FromUtc)
      AND   (@ToUtc      IS NULL OR v.CreatedUtc <  @ToUtc)
    ORDER BY v.CreatedUtc DESC, v.RunId DESC
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);   -- optional filters: avoid parameter-sniffing a bad plan
END
GO

/* ---------- Dashboard: per-location stats for a period ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetDashboardSummary
    @FromUtc DATETIME2(0),
    @ToUtc   DATETIME2(0)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  l.LocationId,
            l.Name                                         AS LocationName,
            l.IsEnabled,
            COUNT(r.RunId)                                 AS RunCount,
            ISNULL(SUM(r.MismatchedCount), 0)              AS MismatchedLines,
            ISNULL(SUM(r.MissingInFarmCount + r.NotInErpCount), 0) AS MissingLines,
            SUM(CASE WHEN r.Status = N'Completed' THEN 1 ELSE 0 END) AS PendingReview,
            MAX(r.CreatedUtc)                              AS LastRunUtc,
            CAST(ISNULL(AVG(CASE WHEN (r.MatchedCount + r.MismatchedCount) = 0 THEN NULL
                     ELSE 100.0 * r.MatchedCount / (r.MatchedCount + r.MismatchedCount) END), 0) AS DECIMAL(5,1)) AS AvgAccuracyPct
    FROM    dbo.Locations l
    LEFT JOIN dbo.ReconciliationRuns r
           ON r.LocationId = l.LocationId
          AND r.CreatedUtc >= @FromUtc
          AND r.CreatedUtc <  @ToUtc
    GROUP BY l.LocationId, l.Name, l.IsEnabled
    ORDER BY l.Name;
END
GO

/* ---------- Variance report for one run (largest differences first) ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_GetRunVarianceReport
    @RunId INT,
    @Top   INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
            ln.ItemCode, ln.ItemName, ln.Category, ln.ErpQty, ln.FarmQty, ln.Difference,
            ABS(ln.Difference) AS AbsDifference
    FROM    dbo.ReconciliationLines ln
    WHERE   ln.RunId = @RunId
      AND   ln.Category <> N'Matched'
    ORDER BY ABS(ln.Difference) DESC, ln.ItemCode;
END
GO

/* ---------- Login attempt bookkeeping + lockout (atomic) ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_RecordLoginAttempt
    @Username       NVARCHAR(50),
    @Succeeded      BIT,
    @MaxAttempts    INT = 5,
    @LockoutMinutes INT = 15
AS
BEGIN
    SET NOCOUNT ON;

    IF @Succeeded = 1
        UPDATE dbo.AppUsers
           SET FailedLoginCount = 0, LockoutEndUtc = NULL, LastLoginUtc = SYSUTCDATETIME()
         WHERE Username = @Username;
    ELSE
        UPDATE dbo.AppUsers
           SET FailedLoginCount = FailedLoginCount + 1,
               LockoutEndUtc    = CASE WHEN FailedLoginCount + 1 >= @MaxAttempts
                                       THEN DATEADD(MINUTE, @LockoutMinutes, SYSUTCDATETIME())
                                       ELSE LockoutEndUtc END
         WHERE Username = @Username;
END
GO

/* ---------- Audit write ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_WriteAuditLog
    @Username      NVARCHAR(50),
    @Action        NVARCHAR(50),
    @Entity        NVARCHAR(50)   = NULL,
    @EntityId      NVARCHAR(50)   = NULL,
    @Details       NVARCHAR(2000) = NULL,
    @IpAddress     NVARCHAR(45)   = NULL,
    @CorrelationId NVARCHAR(64)   = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.AuditLogs (Username, Action, Entity, EntityId, Details, IpAddress, CorrelationId)
    VALUES (@Username, @Action, @Entity, @EntityId, @Details, @IpAddress, @CorrelationId);
END
GO

/* ---------- Housekeeping: integration log retention (run from SQL Agent) ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_PurgeIntegrationLogs
    @RetentionDays INT = 180
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoff DATETIME2(3) = DATEADD(DAY, -@RetentionDays, SYSUTCDATETIME());
    DECLARE @deleted INT = 1, @total INT = 0;

    -- Delete in batches to keep the transaction log and lock footprint small.
    WHILE @deleted > 0
    BEGIN
        DELETE TOP (5000) FROM dbo.IntegrationLogs WHERE CreatedUtc < @cutoff;
        SET @deleted = @@ROWCOUNT;
        SET @total += @deleted;
    END

    SELECT @total AS DeletedRows;
END
GO
