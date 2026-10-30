using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Models;
using System.Data;

namespace PrimaFarm.StockReconciler.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<ReconciliationLine> ReconciliationLines => Set<ReconciliationLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();
    public DbSet<MockFinanceJournal> MockFinanceJournals => Set<MockFinanceJournal>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Schema is owned by the T-SQL scripts in /database; EF only maps to it.
        b.Entity<Location>(e =>
        {
            e.ToTable("Locations");
            e.Property(x => x.Tolerance).HasPrecision(18, 4);
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("AppUsers");
            e.HasKey(x => x.UserId);
        });

        b.Entity<ReconciliationRun>(e =>
        {
            e.ToTable("ReconciliationRuns");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RowVer).IsRowVersion();
            e.Property(x => x.TotalErpQty).HasPrecision(18, 4);
            e.Property(x => x.TotalFarmQty).HasPrecision(18, 4);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReconciliationLine>(e =>
        {
            e.ToTable("ReconciliationLines");
            e.HasKey(x => x.LineId);
            e.Property(x => x.ErpQty).HasPrecision(18, 4);
            e.Property(x => x.FarmQty).HasPrecision(18, 4);
            e.Property(x => x.Difference).HasPrecision(18, 4);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs", t => t.HasTrigger("TR_AuditLogs_PreventChange"));
            e.HasKey(x => x.AuditId);
        });

        b.Entity<IntegrationLog>(e =>
        {
            e.ToTable("IntegrationLogs");
            e.HasKey(x => x.IntegrationLogId);
        });

        b.Entity<MockFinanceJournal>(e =>
        {
            e.ToTable("MockFinanceJournals");
            e.HasKey(x => x.JournalId);
            e.Property(x => x.NetVarianceQty).HasPrecision(18, 4);
        });

        b.Entity<RunSearchRow>().HasNoKey().ToView(null);
        b.Entity<DashboardRow>().HasNoKey().ToView(null);
        b.Entity<VarianceRow>().HasNoKey().ToView(null);
    }

    // ---- Stored procedure wrappers (always parameterised; never string-concatenated SQL) ----

    public async Task<string> GetNextRunReferenceAsync(CancellationToken ct = default)
    {
        var output = new SqlParameter("@RunReference", SqlDbType.NVarChar, 30) { Direction = ParameterDirection.Output };
        await Database.ExecuteSqlRawAsync("EXEC dbo.usp_GetNextRunReference @RunReference OUTPUT", [output], ct);
        return (string)output.Value;
    }

    public Task<List<RunSearchRow>> SearchRunsAsync(int? locationId, string? status, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken ct = default) =>
        Set<RunSearchRow>()
            .FromSqlInterpolated($"EXEC dbo.usp_SearchRuns @LocationId={locationId}, @Status={status}, @FromUtc={fromUtc}, @ToUtc={toUtc}, @PageNumber={page}, @PageSize={pageSize}")
            .ToListAsync(ct);

    public Task<List<DashboardRow>> GetDashboardAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        Set<DashboardRow>()
            .FromSqlInterpolated($"EXEC dbo.usp_GetDashboardSummary @FromUtc={fromUtc}, @ToUtc={toUtc}")
            .ToListAsync(ct);

    public Task<List<VarianceRow>> GetVarianceReportAsync(int runId, int top, CancellationToken ct = default) =>
        Set<VarianceRow>()
            .FromSqlInterpolated($"EXEC dbo.usp_GetRunVarianceReport @RunId={runId}, @Top={top}")
            .ToListAsync(ct);

    public Task RecordLoginAttemptAsync(string username, bool succeeded, int maxAttempts, int lockoutMinutes, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.usp_RecordLoginAttempt @Username={username}, @Succeeded={succeeded}, @MaxAttempts={maxAttempts}, @LockoutMinutes={lockoutMinutes}", ct);

    public Task WriteAuditAsync(string username, string action, string? entity, string? entityId, string? details,
        string? ip, string? correlationId, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"EXEC dbo.usp_WriteAuditLog @Username={username}, @Action={action}, @Entity={entity}, @EntityId={entityId}, @Details={details}, @IpAddress={ip}, @CorrelationId={correlationId}", ct);
}
