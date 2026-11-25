using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShipmentReport.Services;

namespace ShipmentReport.Pages;

public class IndexModel(DatasetStore store, ILogger<IndexModel> log) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Ds { get; set; }

    public ReportFilter Filter { get; private set; } = new();
    public Dataset? Data { get; private set; }
    public List<ShipmentRow> Rows { get; private set; } = [];
    public ReportSummary? Summary { get; private set; }
    public string? Error { get; private set; }

    public void OnGet(ReportFilter filter)
    {
        Filter = filter;
        Data = store.Get(Ds);
        if (Data is null)
        {
            if (Ds is not null) Error = "That upload has expired. Please import the file again.";
            return;
        }
        Rows = Report.Filter(Data.Rows, Filter);
        Summary = Report.Summarize(Rows, Filter.DateField, Filter.Group);
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return Fail("Choose an .xlsx file to import.");
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return Fail("Only .xlsx files are supported.");
        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;
            var ds = ExcelImporter.Read(ms, Path.GetFileName(file.FileName));
            if (ds.Rows.Count == 0) return Fail("The file has a header row but no data rows.");
            return RedirectToPage(new { ds = store.Add(ds) });
        }
        catch (InvalidDataException ex) // our own messages, e.g. missing header columns
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Import failed for {File}", file.FileName);
            return Fail("Couldn't read that file. Is it a valid .xlsx workbook?");
        }

        IActionResult Fail(string message)
        {
            Error = message;
            return Page();
        }
    }

    /// <summary>The page posts the current filters plus PNGs of its charts (as data URLs).</summary>
    public IActionResult OnPostExport(ReportFilter filter, string format, Dictionary<string, string>? charts)
    {
        var ds = store.Get(Ds);
        if (ds is null) return RedirectToPage(new { ds = Ds });
        var rows = Report.Filter(ds.Rows, filter);
        var summary = Report.Summarize(rows, filter.DateField, filter.Group);
        var images = (charts ?? []).Select(kv => DecodePng(kv.Key, kv.Value)).OfType<ChartImage>().ToList();
        var name = $"shipment-report-{DateTime.Now:yyyyMMdd-HHmm}";

        return format == "pdf"
            ? File(ReportExporter.ToPdf(ds, rows, filter, summary, images), "application/pdf", name + ".pdf")
            : File(ReportExporter.ToExcel(ds, rows, filter, summary, images),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name + ".xlsx");
    }

    static ChartImage? DecodePng(string title, string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        if (!dataUrl.StartsWith(prefix)) return null;
        try { return new ChartImage(title, Convert.FromBase64String(dataUrl[prefix.Length..])); }
        catch (FormatException) { return null; }
    }

    /// <summary>Current URL with one filter changed or removed (null).</summary>
    public string LinkWith(string key, string? value)
    {
        var q = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        if (value is null) q.Remove(key); else q[key] = value;
        return Url.Page("/Index", q.Where(kv => kv.Value != "").ToDictionary(kv => kv.Key, kv => (object?)kv.Value))!;
    }
}
