/* =============================================================================
   Prima Three Acre Farms - Stock Reconciler
   900_backup_and_maintenance.sql : business-continuity jobs for the DBA team.
   NOT run by the application. Schedule through SQL Server Agent (full instance).

   Suggested schedule
     - Full backup        : daily 01:00
     - Differential       : every 6 hours
     - Log backup         : every 15 minutes (FULL recovery model)
     - Integrity check    : weekly Sunday 02:00
     - Log retention purge: weekly
   ============================================================================= */
SET NOCOUNT ON;
GO

DECLARE @db     SYSNAME       = N'PrimaFarmStockDb';
DECLARE @folder NVARCHAR(260) = N'D:\SQLBackups\PrimaFarmStockDb\';   -- change per server
DECLARE @stamp  NVARCHAR(20)  = FORMAT(SYSDATETIME(), N'yyyyMMdd_HHmmss');
DECLARE @file   NVARCHAR(400) = @folder + @db + N'_FULL_' + @stamp + N'.bak';

-- 1) Full backup with checksum so corruption is detected at backup time.
BACKUP DATABASE @db
    TO DISK = @file
    WITH CHECKSUM, INIT, STATS = 10, NAME = N'PrimaFarmStockDb full backup';

-- 2) Verify the backup is restorable (does not restore, only validates).
RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM;
GO

-- 3) Weekly integrity check.
DBCC CHECKDB (N'PrimaFarmStockDb') WITH NO_INFOMSGS, ALL_ERRORMSGS;
GO

-- 4) Housekeeping: keep 180 days of integration payloads (audit logs are kept permanently).
EXEC PrimaFarmStockDb.dbo.usp_PurgeIntegrationLogs @RetentionDays = 180;
GO

/* ---------- Restore drill (DR) - run on the standby server, never on production ----------
RESTORE DATABASE PrimaFarmStockDb_DR
    FROM DISK = N'D:\SQLBackups\PrimaFarmStockDb\PrimaFarmStockDb_FULL_yyyyMMdd_HHmmss.bak'
    WITH MOVE N'PrimaFarmStockDb'     TO N'D:\SQLData\PrimaFarmStockDb_DR.mdf',
         MOVE N'PrimaFarmStockDb_log' TO N'D:\SQLData\PrimaFarmStockDb_DR_log.ldf',
         RECOVERY, STATS = 10;
------------------------------------------------------------------------------------------- */
