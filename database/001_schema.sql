/* =============================================================================
   Prima Three Acre Farms - Stock Reconciler
   001_schema.sql : tables, constraints, indexes, sequence, audit protection
   Target        : SQL Server 2016 SP1+ (compatibility level 130)
   Idempotent    : safe to re-run; only creates objects that do not exist.
   ============================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---------- Locations (farms) ---------- */
IF OBJECT_ID(N'dbo.Locations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Locations
    (
        LocationId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,
        Code                  NVARCHAR(30)  NOT NULL,
        Name                  NVARCHAR(100) NOT NULL,
        ErpLocationNo         NVARCHAR(10)  NULL,          -- e.g. 616 = Kosgama in the ERP
        IsEnabled             BIT           NOT NULL CONSTRAINT DF_Locations_IsEnabled DEFAULT (1),
        Status                NVARCHAR(40)  NOT NULL CONSTRAINT DF_Locations_Status DEFAULT (N'Ready'),
        Notes                 NVARCHAR(400) NULL,
        FarmSheetMode         NVARCHAR(20)  NOT NULL CONSTRAINT DF_Locations_FarmSheetMode DEFAULT (N'AllSheets'),
        ExcludedSheets        NVARCHAR(400) NULL,          -- comma separated, supports * wildcard
        Tolerance             DECIMAL(18,4) NOT NULL CONSTRAINT DF_Locations_Tolerance DEFAULT (0),
        IncludeZeroErpMissing BIT           NOT NULL CONSTRAINT DF_Locations_IncludeZero DEFAULT (1),
        CONSTRAINT UQ_Locations_Code UNIQUE (Code),
        CONSTRAINT CK_Locations_FarmSheetMode CHECK (FarmSheetMode IN (N'AllSheets', N'LastSheet'))
    );
END
GO

/* ---------- Application users ---------- */
IF OBJECT_ID(N'dbo.AppUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppUsers
    (
        UserId           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY,
        Username         NVARCHAR(50)  NOT NULL,
        DisplayName      NVARCHAR(100) NOT NULL,
        PasswordHash     NVARCHAR(256) NOT NULL,           -- PBKDF2-SHA256$iter$salt$hash, never plain text
        Role             NVARCHAR(20)  NOT NULL,
        IsActive         BIT           NOT NULL CONSTRAINT DF_AppUsers_IsActive DEFAULT (1),
        FailedLoginCount INT           NOT NULL CONSTRAINT DF_AppUsers_Failed DEFAULT (0),
        LockoutEndUtc    DATETIME2(0)  NULL,
        LastLoginUtc     DATETIME2(0)  NULL,
        CreatedUtc       DATETIME2(0)  NOT NULL CONSTRAINT DF_AppUsers_Created DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_AppUsers_Username UNIQUE (Username),
        CONSTRAINT CK_AppUsers_Role CHECK (Role IN (N'Admin', N'Officer', N'Auditor'))
    );
END
GO

/* ---------- Reference number sequence ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'RunReferenceSeq' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.RunReferenceSeq AS INT START WITH 1 INCREMENT BY 1 NO CYCLE CACHE 20;
GO

/* ---------- Reconciliation runs (header) ---------- */
IF OBJECT_ID(N'dbo.ReconciliationRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReconciliationRuns
    (
        RunId              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReconciliationRuns PRIMARY KEY,
        RunReference       NVARCHAR(30)  NOT NULL,
        LocationId         INT           NOT NULL,
        ErpSource          NVARCHAR(20)  NOT NULL,         -- Upload | Api | AS400
        ErpFileName        NVARCHAR(260) NULL,
        FarmFileNames      NVARCHAR(800) NULL,
        Status             NVARCHAR(20)  NOT NULL CONSTRAINT DF_Runs_Status DEFAULT (N'Completed'),
        MatchedCount       INT           NOT NULL,
        MismatchedCount    INT           NOT NULL,
        MissingInFarmCount INT           NOT NULL,
        NotInErpCount      INT           NOT NULL,
        SummedLineCount    INT           NOT NULL,
        TotalErpQty        DECIMAL(18,4) NOT NULL,
        TotalFarmQty       DECIMAL(18,4) NOT NULL,
        CreatedBy          NVARCHAR(50)  NOT NULL,
        CreatedUtc         DATETIME2(0)  NOT NULL CONSTRAINT DF_Runs_Created DEFAULT (SYSUTCDATETIME()),
        ReviewedBy         NVARCHAR(50)  NULL,
        ReviewedUtc        DATETIME2(0)  NULL,
        ReviewComment      NVARCHAR(500) NULL,
        FinanceReference   NVARCHAR(50)  NULL,
        RowVer             ROWVERSION    NOT NULL,          -- optimistic concurrency for approvals
        CONSTRAINT UQ_Runs_Reference UNIQUE (RunReference),
        CONSTRAINT FK_Runs_Locations FOREIGN KEY (LocationId) REFERENCES dbo.Locations (LocationId),
        CONSTRAINT CK_Runs_Status CHECK (Status IN (N'Completed', N'Approved', N'Rejected', N'Posted', N'PostingFailed')),
        CONSTRAINT CK_Runs_ErpSource CHECK (ErpSource IN (N'Upload', N'Api', N'AS400'))
    );

    CREATE NONCLUSTERED INDEX IX_Runs_Location_Created
        ON dbo.ReconciliationRuns (LocationId, CreatedUtc DESC)
        INCLUDE (RunReference, Status, MismatchedCount);

    CREATE NONCLUSTERED INDEX IX_Runs_Status
        ON dbo.ReconciliationRuns (Status, CreatedUtc DESC);
END
GO

/* ---------- Reconciliation lines (detail) ---------- */
IF OBJECT_ID(N'dbo.ReconciliationLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReconciliationLines
    (
        LineId        BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReconciliationLines PRIMARY KEY,
        RunId         INT            NOT NULL,
        ItemCode      NVARCHAR(50)   NOT NULL,
        ItemName      NVARCHAR(200)  NULL,
        Category      NVARCHAR(20)   NOT NULL,
        ErpQty        DECIMAL(18,4)  NULL,
        FarmQty       DECIMAL(18,4)  NULL,
        Difference    DECIMAL(18,4)  NOT NULL,
        FarmLineCount INT            NOT NULL CONSTRAINT DF_Lines_FarmLineCount DEFAULT (0),
        SourceRows    NVARCHAR(1000) NULL,
        CONSTRAINT FK_Lines_Runs FOREIGN KEY (RunId) REFERENCES dbo.ReconciliationRuns (RunId) ON DELETE CASCADE,
        CONSTRAINT CK_Lines_Category CHECK (Category IN (N'Matched', N'Mismatched', N'MissingInFarm', N'NotInErp'))
    );

    CREATE NONCLUSTERED INDEX IX_Lines_Run_Category
        ON dbo.ReconciliationLines (RunId, Category)
        INCLUDE (ItemCode, ErpQty, FarmQty, Difference);
END
GO

/* ---------- Audit log (append-only) ---------- */
IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        AuditId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        EventUtc      DATETIME2(3)   NOT NULL CONSTRAINT DF_Audit_EventUtc DEFAULT (SYSUTCDATETIME()),
        Username      NVARCHAR(50)   NOT NULL,
        Action        NVARCHAR(50)   NOT NULL,
        Entity        NVARCHAR(50)   NULL,
        EntityId      NVARCHAR(50)   NULL,
        Details       NVARCHAR(2000) NULL,
        IpAddress     NVARCHAR(45)   NULL,
        CorrelationId NVARCHAR(64)   NULL
    );

    CREATE NONCLUSTERED INDEX IX_Audit_EventUtc ON dbo.AuditLogs (EventUtc DESC) INCLUDE (Username, Action);
END
GO

-- Audit evidence must not be altered or removed from the application side.
CREATE OR ALTER TRIGGER dbo.TR_AuditLogs_PreventChange
ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, N'AuditLogs is append-only. Updates and deletes are not permitted.', 1;
END
GO

/* ---------- Integration log (inbound / outbound calls) ---------- */
IF OBJECT_ID(N'dbo.IntegrationLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IntegrationLogs
    (
        IntegrationLogId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IntegrationLogs PRIMARY KEY,
        CreatedUtc       DATETIME2(3)   NOT NULL CONSTRAINT DF_IntLog_Created DEFAULT (SYSUTCDATETIME()),
        TargetSystem     NVARCHAR(50)   NOT NULL,
        Direction        NVARCHAR(10)   NOT NULL,
        Operation        NVARCHAR(100)  NOT NULL,
        RunId            INT            NULL,
        IdempotencyKey   NVARCHAR(64)   NULL,
        HttpStatus       INT            NULL,
        Succeeded        BIT            NOT NULL,
        DurationMs       INT            NOT NULL,
        RequestPayload   NVARCHAR(MAX)  NULL,
        ResponsePayload  NVARCHAR(MAX)  NULL,
        ErrorMessage     NVARCHAR(1000) NULL,
        CorrelationId    NVARCHAR(64)   NULL,
        CONSTRAINT CK_IntLog_Direction CHECK (Direction IN (N'Inbound', N'Outbound'))
    );

    CREATE NONCLUSTERED INDEX IX_IntLog_Created ON dbo.IntegrationLogs (CreatedUtc DESC) INCLUDE (TargetSystem, Succeeded);
END
GO

/* ---------- Simulated finance system journal store (mock core finance / bank API) ---------- */
IF OBJECT_ID(N'dbo.MockFinanceJournals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MockFinanceJournals
    (
        JournalId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MockFinanceJournals PRIMARY KEY,
        JournalNo       NVARCHAR(30)  NOT NULL,
        IdempotencyKey  NVARCHAR(64)  NOT NULL,
        SourceReference NVARCHAR(30)  NOT NULL,
        LineCount       INT           NOT NULL,
        NetVarianceQty  DECIMAL(18,4) NOT NULL,
        Narrative      NVARCHAR(200) NULL,
        ReceivedUtc     DATETIME2(0)  NOT NULL CONSTRAINT DF_MockJ_Received DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_MockFinanceJournals_Idem UNIQUE (IdempotencyKey)
    );
END
GO
