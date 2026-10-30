/* =============================================================================
   Prima Three Acre Farms - Stock Reconciler
   003_seed.sql : reference data (farm locations) and initial user accounts.
   MERGE keeps it idempotent. Change the default passwords after first sign-in.
   ============================================================================= */
SET NOCOUNT ON;
GO

MERGE dbo.Locations AS t
USING (VALUES
    (N'kosgama',     N'Kosgama',     N'616', 1, N'Ready',                 N'Multi-sheet farm workbook (non-product, chick master, stationery, equipment).', N'AllSheets', N'Sheet1'),
    (N'makuluwatta', N'Makuluwatta', N'661', 1, N'Ready',                 N'Monthly side-by-side tables. Uses the last month sheet only.',                 N'LastSheet', N'INVENT*'),
    (N'wewalpanawa', N'Wewalpanawa', N'685', 1, N'Ready',                 N'Multiple farm categories in one file (non-product, uniform, spare parts).',    N'AllSheets', NULL),
    (N'meegoda',     N'Meegoda',     N'611', 0, N'Awaiting farm sample',  N'ERP sample available. Farm format pending.',                                   N'AllSheets', NULL),
    (N'eswatta',     N'Eswatta',     N'656', 0, N'Awaiting farm sample',  N'ERP sample available. Farm format pending.',                                   N'AllSheets', NULL),
    (N'halwathura',  N'Halwathura',  NULL,   0, N'Awaiting samples',      N'ERP and farm samples pending.',                                                N'AllSheets', NULL)
) AS s (Code, Name, ErpLocationNo, IsEnabled, Status, Notes, FarmSheetMode, ExcludedSheets)
ON t.Code = s.Code
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Code, Name, ErpLocationNo, IsEnabled, Status, Notes, FarmSheetMode, ExcludedSheets)
    VALUES (s.Code, s.Name, s.ErpLocationNo, s.IsEnabled, s.Status, s.Notes, s.FarmSheetMode, s.ExcludedSheets);
GO

-- Default passwords: Admin@2026 / Officer@2026 / Auditor@2026 (PBKDF2-SHA256, 600,000 iterations)
MERGE dbo.AppUsers AS t
USING (VALUES
    (N'admin',   N'Administrator',     N'Admin',   N'PBKDF2-SHA256$600000$ml3RdACtEY9AdCIjFiy6lA==$llLXb7oN+fDZv0kV2gLE7ovLs8lffqz0bjxi4OOLgig='),
    (N'officer', N'Stock Officer',     N'Officer', N'PBKDF2-SHA256$600000$hPxWQTCOnzTxQINzLsJvzA==$fSnioLkt1BH0mliXO3UfM0QvxhtyBL8jlZaE6XmhsAY='),
    (N'auditor', N'Internal Auditor',  N'Auditor', N'PBKDF2-SHA256$600000$YOMTQqZtGARM3I/QhRjffA==$6yi5xra5+S+VU9RODK/fKTjunj2BcFNN43yvkuN4pYk=')
) AS s (Username, DisplayName, Role, PasswordHash)
ON t.Username = s.Username
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Username, DisplayName, Role, PasswordHash)
    VALUES (s.Username, s.DisplayName, s.Role, s.PasswordHash);
GO
