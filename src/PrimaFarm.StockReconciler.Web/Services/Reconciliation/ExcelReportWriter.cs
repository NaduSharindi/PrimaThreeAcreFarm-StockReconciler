using ClosedXML.Excel;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Services.Reconciliation;

/// <summary>Builds the colour-coded Excel report (same tabs as the original Python tool).</summary>
public static class ExcelReportWriter
{
    private static readonly XLColor HeaderBlue = XLColor.FromHtml("#0B4F8A");
    private static readonly XLColor Green = XLColor.FromHtml("#C6EFCE");
    private static readonly XLColor Red = XLColor.FromHtml("#FFC7CE");
    private static readonly XLColor Yellow = XLColor.FromHtml("#FFEB9C");

    public static byte[] Build(ReconciliationRun run)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Summary");
        ws.Cell("B2").Value = "Prima Three Acre Farms - Stock Reconciliation Report";
        ws.Cell("B2").Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(HeaderBlue);

        var meta = new (string Label, string Value)[]
        {
            ("Reference", run.RunReference),
            ("Location", run.Location?.Name ?? ""),
            ("ERP / system file", $"{run.ErpFileName} ({run.ErpSource})"),
            ("Farm file(s)", run.FarmFileNames ?? ""),
            ("Prepared by", $"{run.CreatedBy} on {run.CreatedUtc:yyyy-MM-dd HH:mm} UTC"),
            ("Status", run.Status + (run.ReviewedBy is null ? "" : $" by {run.ReviewedBy}")),
        };
        int r = 4;
        foreach (var (label, value) in meta)
        {
            ws.Cell(r, 2).Value = label;
            ws.Cell(r, 2).Style.Font.SetBold();
            ws.Cell(r++, 3).Value = value;
        }

        r++;
        ws.Cell(r, 2).Value = "Category";
        ws.Cell(r, 3).Value = "Count";
        StyleHeader(ws.Range(r, 2, r, 3));
        var summary = new (string, int, XLColor)[]
        {
            ("Matched items (quantities agree)", run.MatchedCount, Green),
            ("Mismatched quantities", run.MismatchedCount, Red),
            ("Missing in farm file (in ERP, not reported)", run.MissingInFarmCount, Yellow),
            ("Item codes not in ERP system", run.NotInErpCount, Red),
            ("Items needing multiple farm lines summed", run.SummedLineCount, Yellow),
        };
        foreach (var (label, count, colour) in summary)
        {
            r++;
            ws.Cell(r, 2).Value = label;
            ws.Cell(r, 3).Value = count;
            ws.Range(r, 2, r, 3).Style.Fill.SetBackgroundColor(colour);
        }
        ws.Columns(2, 3).AdjustToContents();

        string[] full = ["Item Code", "Item Name", "ERP Qty", "Farm Qty", "Difference", "Farm Lines", "Source Rows"];
        AddSheet(wb, "Mismatched Qty", full, run.Lines.Where(l => l.Category == LineCategories.Mismatched), Red);
        AddSheet(wb, "Missing In Farm File", full, run.Lines.Where(l => l.Category == LineCategories.MissingInFarm), Yellow);
        AddSheet(wb, "Not In ERP System", full, run.Lines.Where(l => l.Category == LineCategories.NotInErp), Red);
        AddSheet(wb, "Lines Needing Summing", full, run.Lines.Where(l => l.FarmLineCount > 1), Yellow);
        AddSheet(wb, "Matched (OK)", full, run.Lines.Where(l => l.Category == LineCategories.Matched), Green);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void AddSheet(XLWorkbook wb, string name, string[] headers, IEnumerable<ReconciliationLine> lines, XLColor fill)
    {
        var ws = wb.Worksheets.Add(name);
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        StyleHeader(ws.Range(1, 1, 1, headers.Length));

        int r = 2;
        foreach (var l in lines.OrderBy(l => l.ItemCode))
        {
            ws.Cell(r, 1).Value = l.ItemCode;
            ws.Cell(r, 2).Value = l.ItemName;
            ws.Cell(r, 3).Value = l.ErpQty;
            ws.Cell(r, 4).Value = l.FarmQty;
            ws.Cell(r, 5).Value = l.Difference;
            ws.Cell(r, 6).Value = l.FarmLineCount;
            ws.Cell(r, 7).Value = l.SourceRows;
            ws.Range(r, 1, r, headers.Length).Style.Fill.SetBackgroundColor(fill);
            r++;
        }
        ws.Range(1, 1, Math.Max(r - 1, 1), headers.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 500), 10, 60);
    }

    private static void StyleHeader(IXLRange range) =>
        range.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(HeaderBlue)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
}
