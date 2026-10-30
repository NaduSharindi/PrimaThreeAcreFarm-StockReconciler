using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Services.Reconciliation;

/// <summary>One stock line as read from a source file (before aggregation).</summary>
public sealed record StockLine(string ItemCode, string ItemName, decimal Qty, string SourceFile, string SourceSheet, int SourceRow);

public sealed class StockFileOptions
{
    public string FarmSheetMode { get; init; } = FarmSheetModes.AllSheets;
    public IReadOnlyList<string> ExcludedSheets { get; init; } = [];

    public static StockFileOptions ForErp() => new();

    public static StockFileOptions ForFarm(Location loc) => new()
    {
        FarmSheetMode = loc.FarmSheetMode,
        ExcludedSheets = (loc.ExcludedSheets ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    };
}

public class StockFileFormatException(string message) : Exception(message);

/// <summary>
/// Reads stock lines from ERP exports and farm submission files.
///  - .xlsx / .xlsm : multi-sheet workbooks, header row anywhere in the first 20 rows,
///                    several side-by-side tables per sheet, flexible quantity headers.
///  - .csv          : same header detection as Excel.
///  - .txt / .dat   : IBM i (AS400) fixed-width extract with header/trailer control totals.
/// </summary>
public static partial class StockFileReader
{
    public static readonly string[] SupportedExtensions = [".xlsx", ".xlsm", ".csv", ".txt", ".dat"];

    private const int HeaderScanRows = 20;

    private static readonly HashSet<string> CodeHeaders =
        ["item no", "item no.", "item number", "item code", "itemcode", "code", "item nos", "item #"];

    private static readonly HashSet<string> NameHeaders =
        ["name", "description", "item name", "item description", "item"];

    private static readonly Regex[] QtyAvoid =
    [
        new(@"^farm\s*qty", RegexOptions.IgnoreCase), new(@"^o\s*/\s*b\b", RegexOptions.IgnoreCase),
        new(@"^opening", RegexOptions.IgnoreCase),    new(@"^rec(e|i)?(i|e)?v", RegexOptions.IgnoreCase),
        new(@"^usage", RegexOptions.IgnoreCase),      new(@"^issued", RegexOptions.IgnoreCase),
        new(@"^send", RegexOptions.IgnoreCase),       new(@"^alloc", RegexOptions.IgnoreCase),
    ];

    [GeneratedRegex(@"^\d{1,2}[./-]\d{1,2}[./-]\d{2,4}$")]
    private static partial Regex DateHeader();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[A-Za-z0-9\s\-./_]+$")]
    private static partial Regex SimpleCode();

    public static IReadOnlyList<StockLine> Read(Stream stream, string fileName, StockFileOptions options)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ReadExcel(stream, fileName, options),
            ".csv" => ParseGrid(ReadCsv(stream), fileName, "csv"),
            ".txt" or ".dat" => ReadAs400FixedWidth(stream, fileName),
            _ => throw new StockFileFormatException($"Unsupported file type '{ext}'.")
        };
    }

    // ------------------------------------------------------------------ Excel

    private static List<StockLine> ReadExcel(Stream stream, string fileName, StockFileOptions options)
    {
        XLWorkbook opened;
        try { opened = new XLWorkbook(stream); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new StockFileFormatException($"'{fileName}' could not be opened as an Excel workbook. Is it password-protected or corrupt?");
        }
        using var wb = opened;
        var sheets = wb.Worksheets
            .Where(ws => !options.ExcludedSheets.Any(p => GlobMatch(ws.Name, p)))
            .ToList();
        if (options.FarmSheetMode == FarmSheetModes.LastSheet && sheets.Count > 0)
            sheets = [sheets[^1]];

        var lines = new List<StockLine>();
        foreach (var ws in sheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;
            int lastRow = used.LastRow().RowNumber(), lastCol = used.LastColumn().ColumnNumber();

            var grid = new List<object?[]>(lastRow);
            for (int r = 1; r <= lastRow; r++)
            {
                var row = new object?[lastCol];
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = ws.Cell(r, c);
                    var v = cell.HasFormula ? cell.CachedValue : cell.Value;
                    row[c - 1] = v.Type switch
                    {
                        XLDataType.Number => v.GetNumber(),
                        XLDataType.Text => v.GetText(),
                        XLDataType.DateTime => v.GetDateTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                        XLDataType.Boolean => v.GetBoolean().ToString(),
                        _ => null
                    };
                }
                grid.Add(row);
            }
            lines.AddRange(ParseGrid(grid, fileName, ws.Name));
        }
        return lines;
    }

    // ------------------------------------------------------------------ CSV

    private static List<object?[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = new List<object?[]>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            rows.Add([.. SplitCsvLine(line)]);
        return rows;
    }

    internal static IEnumerable<string> SplitCsvLine(string line)
    {
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { yield return sb.ToString(); sb.Clear(); }
            else sb.Append(ch);
        }
        yield return sb.ToString();
    }

    // ------------------------------------------------------------------ IBM i / AS400 fixed width

    /// <summary>
    /// Layout of the nightly IBM i extract (one record per line, record type in column 1):
    ///   H | extract date yyyyMMdd (2-9) | location no (10-14)
    ///   D | item no (2-16) | description (17-56) | location (57-61) | on-hand qty (62-74, signed, 3 dp implied)
    ///   T | record count (2-10) | total qty (11-25, signed, 3 dp implied)
    /// The trailer is a control total: the file is rejected if counts or totals do not agree.
    /// </summary>
    private static List<StockLine> ReadAs400FixedWidth(Stream stream, string fileName)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var lines = new List<StockLine>();
        int? trailerCount = null;
        decimal? trailerTotal = null;
        int lineNo = 0;
        string? raw;

        while ((raw = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            switch (raw[0])
            {
                case 'H':
                    break;
                case 'D':
                    if (raw.Length < 74)
                        throw new StockFileFormatException($"AS400 file line {lineNo}: detail record is {raw.Length} chars, expected 74.");
                    var code = CleanCode(raw.Substring(1, 15));
                    if (code.Length == 0)
                        throw new StockFileFormatException($"AS400 file line {lineNo}: item number is blank.");
                    lines.Add(new StockLine(code, raw.Substring(16, 40).Trim(), ParseImpliedDecimal(raw.Substring(61, 13), lineNo),
                        fileName, "AS400", lineNo));
                    break;
                case 'T':
                    if (raw.Length < 25)
                        throw new StockFileFormatException($"AS400 file line {lineNo}: trailer record is too short.");
                    trailerCount = int.Parse(raw.Substring(1, 9), CultureInfo.InvariantCulture);
                    trailerTotal = ParseImpliedDecimal(raw.Substring(10, 15), lineNo);
                    break;
                default:
                    throw new StockFileFormatException($"AS400 file line {lineNo}: unknown record type '{raw[0]}'.");
            }
        }

        if (trailerCount is null)
            throw new StockFileFormatException("AS400 file has no trailer (T) record; the extract may be incomplete.");
        if (trailerCount != lines.Count)
            throw new StockFileFormatException($"AS400 control total mismatch: trailer says {trailerCount} records, file has {lines.Count}.");
        var actualTotal = lines.Sum(l => l.Qty);
        if (trailerTotal != actualTotal)
            throw new StockFileFormatException($"AS400 control total mismatch: trailer qty {trailerTotal}, detail qty {actualTotal}.");
        return lines;
    }

    // Signed numeric with 3 implied decimals, e.g. "000000001500-" = -1.500 (trailing sign, RPG style).
    private static decimal ParseImpliedDecimal(string field, int lineNo)
    {
        var s = field.Trim();
        bool negative = s.EndsWith('-') || s.StartsWith('-');
        s = s.Trim('-', '+');
        if (!long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var units))
            throw new StockFileFormatException($"AS400 file line {lineNo}: '{field}' is not a valid quantity.");
        var value = units / 1000m;
        return negative ? -value : value;
    }

    // ------------------------------------------------------------------ shared grid parsing

    private sealed record Table(int HeaderRow, int CodeCol, int? NameCol, int QtyCol);

    internal static List<StockLine> ParseGrid(IReadOnlyList<object?[]> rows, string fileName, string sheetName)
    {
        var lines = new List<StockLine>();
        var tables = FindTables(rows);
        foreach (var t in tables)
        {
            for (int r = t.HeaderRow + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var code = CleanCode(Cell(row, t.CodeCol));
                if (code.Length == 0) continue;
                var name = t.NameCol is int nc ? Convert.ToString(Cell(row, nc), CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
                lines.Add(new StockLine(code, name, ParseQty(Cell(row, t.QtyCol)), fileName, sheetName, r + 1));
            }
        }
        return lines;
    }

    private static List<Table> FindTables(IReadOnlyList<object?[]> rows)
    {
        for (int r = 0; r < Math.Min(HeaderScanRows, rows.Count); r++)
        {
            var row = rows[r];
            var codeCols = new List<int>();
            for (int c = 0; c < row.Length; c++)
            {
                var h = Norm(row[c]);
                if (CodeHeaders.Contains(h) || (h == "item" && Norm(Cell(row, c + 1)).Contains("name")))
                    codeCols.Add(c);
            }
            if (codeCols.Count == 0) continue;

            var tables = new List<Table>();
            for (int i = 0; i < codeCols.Count; i++)
            {
                int start = codeCols[i], end = i + 1 < codeCols.Count ? codeCols[i + 1] : row.Length;
                int? nameCol = null;
                int bestQty = -1, bestScore = 0;
                for (int c = start + 1; c < end; c++)
                {
                    var h = Norm(row[c]);
                    if (nameCol is null && NameHeaders.Contains(h)) { nameCol = c; continue; }
                    var score = ScoreQtyHeader(h);
                    if (score > 0 && score >= bestScore) { bestScore = score; bestQty = c; }   // ties -> rightmost (latest period)
                }
                if (bestQty >= 0) tables.Add(new Table(r, start, nameCol, bestQty));
            }
            if (tables.Count > 0) return tables;
        }
        return [];
    }

    internal static int ScoreQtyHeader(string h)
    {
        if (h.Length == 0 || QtyAvoid.Any(p => p.IsMatch(h))) return 0;
        int score = 0;
        if (h is "c/b" or "closing balance" or "closing" or "on-hand" or "on hand" or "onhand" or "balance" or "qty" or "quantity") score += 30;
        if (h.StartsWith("balance")) score += 25;
        if (DateHeader().IsMatch(h)) score += 20;
        if (h.Contains("c/b") || h.Contains("closing")) score += 15;
        return score;
    }

    private static object? Cell(object?[] row, int c) => c < row.Length ? row[c] : null;

    private static string Norm(object? v) =>
        v is null ? "" : Whitespace().Replace(Convert.ToString(v, CultureInfo.InvariantCulture)!.Replace(' ', ' '), " ").Trim().ToLowerInvariant();

    internal static string CleanCode(object? v)
    {
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Replace(' ', ' ').Trim() ?? "";
        if (s.Length == 0 || CodeHeaders.Contains(s.ToLowerInvariant()) || NameHeaders.Contains(s.ToLowerInvariant())) return "";
        if (SimpleCode().IsMatch(s)) s = Whitespace().Replace(s, "");     // "EMHKP 0080" -> "EMHKP0080"
        // Real item codes always carry digits; this drops titles, totals and signature lines.
        if (!s.Any(char.IsDigit) || s.Length > 50) return "";
        return s.ToUpperInvariant();
    }

    internal static decimal ParseQty(object? v) => v switch
    {
        null => 0m,
        double d => (decimal)d,
        string s when decimal.TryParse(s.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var q) => q,
        _ => 0m
    };

    internal static bool GlobMatch(string name, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(name.Trim(), regex, RegexOptions.IgnoreCase);
    }
}
