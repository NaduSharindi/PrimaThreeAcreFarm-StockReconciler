using System.Runtime.Serialization;
using System.ServiceModel;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;

namespace PrimaFarm.StockReconciler.Web.Services.Soap;

/// <summary>
/// SOAP 1.1 endpoint for legacy consumers (e.g. VB6 / IBM i jobs that cannot call REST).
/// WSDL: /soap/StockReconciliation.asmx?wsdl  (requires the X-Api-Key header)
/// </summary>
[ServiceContract(Namespace = "http://primagroup.lk/threeacrefarms/stock/v1")]
public interface IStockReconciliationSoapService
{
    [OperationContract]
    RunSummaryContract? GetRunSummary(string runReference);

    [OperationContract]
    LocationContract[] GetLocations();
}

[DataContract(Namespace = "http://primagroup.lk/threeacrefarms/stock/v1")]
public class RunSummaryContract
{
    [DataMember(Order = 1)] public string RunReference { get; set; } = "";
    [DataMember(Order = 2)] public string Location { get; set; } = "";
    [DataMember(Order = 3)] public string Status { get; set; } = "";
    [DataMember(Order = 4)] public int Matched { get; set; }
    [DataMember(Order = 5)] public int Mismatched { get; set; }
    [DataMember(Order = 6)] public int MissingInFarm { get; set; }
    [DataMember(Order = 7)] public int NotInErp { get; set; }
    [DataMember(Order = 8)] public DateTime CreatedUtc { get; set; }
    [DataMember(Order = 9)] public string? FinanceReference { get; set; }
}

[DataContract(Namespace = "http://primagroup.lk/threeacrefarms/stock/v1")]
public class LocationContract
{
    [DataMember(Order = 1)] public string Code { get; set; } = "";
    [DataMember(Order = 2)] public string Name { get; set; } = "";
    [DataMember(Order = 3)] public bool Enabled { get; set; }
    [DataMember(Order = 4)] public string Status { get; set; } = "";
}

public class StockReconciliationSoapService(AppDbContext db) : IStockReconciliationSoapService
{
    public RunSummaryContract? GetRunSummary(string runReference)
    {
        if (string.IsNullOrWhiteSpace(runReference) || runReference.Length > 30) return null;
        return db.ReconciliationRuns.AsNoTracking()
            .Where(r => r.RunReference == runReference)
            .Select(r => new RunSummaryContract
            {
                RunReference = r.RunReference,
                Location = r.Location!.Name,
                Status = r.Status,
                Matched = r.MatchedCount,
                Mismatched = r.MismatchedCount,
                MissingInFarm = r.MissingInFarmCount,
                NotInErp = r.NotInErpCount,
                CreatedUtc = r.CreatedUtc,
                FinanceReference = r.FinanceReference
            })
            .FirstOrDefault();
    }

    public LocationContract[] GetLocations() =>
        db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new LocationContract { Code = l.Code, Name = l.Name, Enabled = l.IsEnabled, Status = l.Status })
            .ToArray();
}
