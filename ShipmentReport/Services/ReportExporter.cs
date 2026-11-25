using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ShipmentReport.Services;

/// <summary>Short English columns shown on the page and in the PDF (the Excel export keeps every original column).</summary>
public static class KeyColumns
{
    static string D(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "";

    public static readonly (string Label, Func<ShipmentRow, string> Value)[] All =
    [
        ("Job", r => r.Job),
        ("Invoice No", r => r.Invoice),
        ("Origin", r => r.Origin),
        ("Carrier", r => r.Carrier),
        ("Container", r => r.Container),
        ("Size", r => r.Size),
        ("Cartons", r => r.Cartons?.ToString("0") ?? ""),
        ("CBM", r => r.Cbm?.ToString("0.##") ?? ""),
        ("ETA", r => D(r.Date("ETA"))),
        ("Doc Complete", r => D(r.Date("DocComplete"))),
        ("Customs Release", r => D(r.Date("Customs"))),
        ("Delivery", r => D(r.Date("Delivery"))),
        ("Docs", r => r.DocRevised ? "Revised" : ""),
        ("Decl.", r => r.Declaration),
    ];
}

public sealed record ChartImage(string Title, byte[] Png);

public static class ReportExporter
{
    static IEnumerable<(string, string)> KpiLines(Kpis k) =>
    [
        ("Rows", k.Rows.ToString()),
        ("Jobs", k.Jobs.ToString()),
        ("Invoices", k.Invoices.ToString()),
        ("Containers", k.Containers.ToString()),
        ("CBM (each invoice once)", k.Cbm.ToString("#,0.##")),
        ("Cartons (each invoice once)", k.Cartons.ToString("#,0")),
        ("Avg days ETA → Customs release", k.AvgDaysToCustoms?.ToString("0.0") ?? "-"),
        ("Avg days ETA → Delivery", k.AvgDaysToDelivery?.ToString("0.0") ?? "-"),
        ("Invoices with revised documents", $"{k.RevisedInvoices} of {k.Invoices}"),
    ];

    public static byte[] ToExcel(Dataset ds, List<ShipmentRow> rows, ReportFilter f, ReportSummary s, List<ChartImage> charts)
    {
        using var wb = new XLWorkbook();

        // Summary: filters, KPIs, chart data, chart pictures.
        var sum = wb.AddWorksheet("Summary");
        sum.Cell(1, 1).Value = "Import Shipment Report";
        sum.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16);
        sum.Cell(2, 1).Value = $"Source: {ds.FileName} · exported {DateTime.Now:yyyy-MM-dd HH:mm}";
        int r = 4;
        sum.Cell(r++, 1).Value = "Filters";
        sum.Cell(r - 1, 1).Style.Font.SetBold();
        var filters = f.Describe().ToList();
        if (filters.Count == 0) sum.Cell(r++, 1).Value = "None (all rows)";
        foreach (var (k, v) in filters) { sum.Cell(r, 1).Value = k; sum.Cell(r++, 2).Value = v; }
        r++;
        sum.Cell(r++, 1).Value = "Summary";
        sum.Cell(r - 1, 1).Style.Font.SetBold();
        foreach (var (k, v) in KpiLines(s.Kpis)) { sum.Cell(r, 1).Value = k; sum.Cell(r++, 2).Value = v; }
        r++;

        void Table(string title, ChartData c)
        {
            sum.Cell(r, 1).Value = title;
            sum.Cell(r++, 1).Style.Font.SetBold();
            for (int i = 0; i < c.Series.Count; i++) sum.Cell(r, i + 2).Value = c.Series[i].Label;
            sum.Row(r++).Style.Font.SetItalic();
            for (int j = 0; j < c.Labels.Count; j++, r++)
            {
                sum.Cell(r, 1).Value = c.Labels[j];
                for (int i = 0; i < c.Series.Count; i++) sum.Cell(r, i + 2).Value = c.Series[i].Values[j];
            }
            r++;
        }
        Table($"Containers per {s.Group.ToLowerInvariant()} by size", s.Daily);
        Table("By origin", s.ByOrigin);
        Table("By carrier", s.ByCarrier);
        Table("Avg lead time in days, by origin", s.LeadTime);
        Table("Document status (invoices)", s.DocStatus);
        Table("Declaration status (invoices)", s.Declaration);
        sum.Column(1).Width = 36;
        sum.Columns(2, 8).Width = 16;

        int picRow = 4;
        foreach (var c in charts)
        {
            sum.Cell(picRow, 10).Value = c.Title;
            sum.Cell(picRow, 10).Style.Font.SetBold();
            using var ms = new MemoryStream(c.Png);
            var pic = sum.AddPicture(ms).MoveTo(sum.Cell(picRow + 1, 10));
            pic.ScaleWidth(560.0 / pic.Width, true);
            picRow += 2 + (int)Math.Ceiling(pic.Height / 20.0);
        }

        // Data: the filtered rows with every original column.
        var data = wb.AddWorksheet("Data");
        for (int c = 0; c < ds.Headers.Count; c++) data.Cell(1, c + 1).Value = ds.Headers[c];
        for (int i = 0; i < rows.Count; i++)
            for (int c = 0; c < ds.Headers.Count; c++)
            {
                var cell = data.Cell(i + 2, c + 1);
                switch (rows[i].Cells[c])
                {
                    case DateTime d:
                        cell.Value = d;
                        cell.Style.DateFormat.Format = d.TimeOfDay == TimeSpan.Zero ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm";
                        break;
                    case double n: cell.Value = n; break;
                    case bool b: cell.Value = b; break;
                    case string t: cell.Value = t; break;
                }
            }
        if (rows.Count > 0)
        {
            var table = data.Range(1, 1, rows.Count + 1, ds.Headers.Count).CreateTable("Shipments");
            table.Theme = XLTableTheme.TableStyleLight9;
        }
        else data.Row(1).Style.Font.SetBold();
        data.Columns().Width = 16;
        data.SheetView.FreezeRows(1);

        using var output = new MemoryStream();
        wb.SaveAs(output);
        return output.ToArray();
    }

    public static byte[] ToPdf(Dataset ds, List<ShipmentRow> rows, ReportFilter f, ReportSummary s, List<ChartImage> charts)
    {
        var filters = f.Describe().ToList();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            // shortcut: QuestPDF's bundled Lato font has no Thai glyphs; ship a Thai .ttf beside the app if data values contain Thai.
            page.DefaultTextStyle(t => t.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text("Import Shipment Report").FontSize(18).Bold().FontColor("#1d4e89");
                col.Item().Text($"Source: {ds.FileName} · exported {DateTime.Now:yyyy-MM-dd HH:mm} · " +
                                (filters.Count == 0 ? "no filters" : string.Join(" · ", filters.Select(x => $"{x.Item1}: {x.Item2}"))))
                    .FontColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Row(row =>
                {
                    foreach (var (k, v) in KpiLines(s.Kpis).Where(x => x.Item1 != "Rows"))
                        row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(c =>
                        {
                            c.Item().Text(k).FontSize(7).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(v).FontSize(12).Bold();
                        });
                });

                // 3 charts per row at a fixed height keeps all six on the first page.
                foreach (var group in charts.Chunk(3))
                    col.Item().ShowEntire().Row(row =>
                    {
                        foreach (var c in group)
                            row.RelativeItem().Padding(4).Column(cc =>
                            {
                                cc.Item().Text(c.Title).Bold();
                                cc.Item().Height(170).AlignCenter().Image(c.Png).FitArea();
                            });
                        for (int i = group.Length; i < 3; i++) row.RelativeItem();
                    });

                col.Item().PageBreak();
                col.Item().Text($"Shipments ({rows.Count} rows)").FontSize(12).Bold();
                col.Item().Table(t =>
                {
                    var cols = KeyColumns.All;
                    t.ColumnsDefinition(cd => { foreach (var _ in cols) cd.RelativeColumn(); });
                    t.Header(h =>
                    {
                        foreach (var c in cols)
                            h.Cell().Background("#1d4e89").Padding(3).Text(c.Label).FontColor(Colors.White).FontSize(7).Bold();
                    });
                    for (int i = 0; i < rows.Count; i++)
                        foreach (var c in cols)
                            t.Cell().Background(i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4)
                                .Padding(3).Text(c.Value(rows[i])).FontSize(7);
                });
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }
}
