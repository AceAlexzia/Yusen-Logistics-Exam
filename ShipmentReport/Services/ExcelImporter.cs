using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ShipmentReport.Services;

public static partial class ExcelImporter
{
    /// <summary>Reads the first sheet. Columns are found by header name, so column order may change.</summary>
    public static Dataset Read(Stream stream, string fileName)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var used = ws.RangeUsed() ?? throw new InvalidDataException("The first sheet is empty.");
        int firstRow = used.FirstRow().RowNumber(), lastRow = used.LastRow().RowNumber();
        int firstCol = used.FirstColumn().ColumnNumber(), lastCol = used.LastColumn().ColumnNumber();

        var headers = Enumerable.Range(firstCol, lastCol - firstCol + 1)
            .Select(c => ws.Cell(firstRow, c).GetString().Trim()).ToList();
        var norm = headers.Select(h => h.ToLowerInvariant()).ToList();

        // Exact header match first, then "starts with".
        int Find(params string[] names)
        {
            foreach (var n in names) { var i = norm.IndexOf(n); if (i >= 0) return i; }
            foreach (var n in names) { var i = norm.FindIndex(h => h.StartsWith(n)); if (i >= 0) return i; }
            return -1;
        }

        int job = Find("job i", "job no", "job"), invoice = Find("invoice no", "invoice");
        int po = norm.FindIndex(h => h == "po" || h.StartsWith("po ") || h.StartsWith("po.") || h.StartsWith("po#")); // not "port…"
        if (job < 0 && invoice < 0)
            throw new InvalidDataException("Couldn't find a 'Job' or 'Invoice No' column in the header row.");
        int origin = Find("origin"), carrier = Find("carrier name", "carrier"), container = Find("ctnr no.", "container no");
        int size = Find("container size"), cartons = Find("cartons"), cbm = Find("cbm");
        int docIssue = Find("shipping document issue"), decl = Find("status declaration");

        var dateCols = DateField.All
            .Select(f => (Field: f, Col: f.HeaderPrefixes.Select(p => norm.FindIndex(h => h.StartsWith(p))).FirstOrDefault(i => i >= 0, -1)))
            .Where(x => x.Col >= 0).ToList();
        var dateColSet = dateCols.Select(x => x.Col).ToHashSet();

        var rows = new List<ShipmentRow>();
        int skipped = 0;
        for (int r = firstRow + 1; r <= lastRow; r++)
        {
            var raw = Enumerable.Range(0, headers.Count).Select(c => ws.Cell(r, firstCol + c).Value).ToArray();
            // Date columns become DateTime, except text like the two-timestamp "Doc Received" which stays as written.
            var cells = raw.Select((v, c) => dateColSet.Contains(c) && v.Type != XLDataType.Text ? ToDate(v) : ToPlain(v)).ToArray();

            string Text(int i) => i < 0 ? "" : Convert.ToString(cells[i], CultureInfo.InvariantCulture)?.Trim() ?? "";
            double? Num(int i) => i >= 0 && cells[i] is double d ? d
                : double.TryParse(Text(i), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

            // A row with no job, invoice or container is junk (e.g. a stray value at the bottom of the sheet).
            if (Text(job) == "" && Text(invoice) == "" && Text(container) == "")
            {
                if (cells.Any(c => c is not null)) skipped++; // fully empty (just formatted) rows aren't worth reporting
                continue;
            }

            rows.Add(new ShipmentRow
            {
                Cells = cells,
                Job = Text(job),
                Invoice = Text(invoice),
                Po = Text(po),
                Origin = Text(origin),
                Carrier = Text(carrier).TrimEnd('.').Trim(), // "CNC LINE." == "CNC LINE"
                Container = Text(container),
                Size = Text(size),
                Cartons = Num(cartons),
                Cbm = Num(cbm),
                DocRevised = Text(docIssue) != "",
                Declaration = Text(decl),
                Dates = dateCols.ToDictionary(x => x.Field.Key, x => ToDate(raw[x.Col])),
            });
        }

        return new Dataset
        {
            FileName = fileName,
            SheetName = ws.Name,
            Headers = headers,
            DateColumns = dateColSet,
            Rows = rows,
            SkippedRows = skipped,
            DateFields = dateCols.Select(x => x.Field).ToList(),
        };
    }

    static object? ToPlain(XLCellValue v) => v.Type switch
    {
        XLDataType.Blank => null,
        XLDataType.Number => v.GetNumber(),
        XLDataType.Boolean => v.GetBoolean(),
        XLDataType.DateTime => v.GetDateTime(),
        XLDataType.TimeSpan => v.GetTimeSpan().ToString(),
        XLDataType.Error => null,
        _ => v.GetText().Trim() is var s && s != "" ? s : null,
    };

    /// <summary>Excel serial numbers, real dates, or text like "2026-08-20 16:44:37 2026-08-20 16:44:37" (first one wins).</summary>
    static DateTime? ToDate(XLCellValue v)
    {
        switch (v.Type)
        {
            case XLDataType.DateTime: return v.GetDateTime();
            case XLDataType.Number:
                var n = v.GetNumber();
                return n is > 0 and < 2958466 ? DateTime.FromOADate(n) : null;
            case XLDataType.Text:
                var m = FirstDate().Match(v.GetText());
                return m.Success && DateTime.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            default: return null;
        }
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}( \d{2}:\d{2}(:\d{2})?)?")]
    private static partial Regex FirstDate();
}
