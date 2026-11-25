using ShipmentReport.Services;

namespace ShipmentReport.Tests;

public class ReportTests
{
    static Dataset Sample()
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "sample.xlsx"));
        return ExcelImporter.Read(s, "sample.xlsx");
    }

    [Fact]
    public void Import_reads_rows_dates_and_skips_junk()
    {
        var ds = Sample();
        Assert.Equal(100, ds.Rows.Count);
        Assert.Equal(1, ds.SkippedRows);
        Assert.Equal(new DateTime(2026, 9, 1), ds.Rows[0].Date("ETA"));
        Assert.Equal(new DateTime(2026, 8, 20, 16, 44, 37), ds.Rows[0].Date("DocReceive"));
        Assert.DoesNotContain(ds.Rows, r => r.Carrier == "CNC LINE.");
        Assert.Equal(9, ds.DateFields.Count);
    }

    [Fact]
    public void Filters_by_date_range_and_search()
    {
        var rows = Sample().Rows;
        var day1 = new DateOnly(2026, 9, 1);
        Assert.Equal(81, Report.Filter(rows, new ReportFilter { From = day1, To = day1 }).Count);
        // Search is exact (case ignored): a partial number matches nothing.
        Assert.Equal(15, Report.Filter(rows, new ReportFilter { Q = " jlb3a746574637-0 " }).Count);
        Assert.Empty(Report.Filter(rows, new ReportFilter { Q = "JLB3A" }));
        Assert.Equal(8, Report.Filter(rows, new ReportFilter { Q = "I260802191" }).Count);
        Assert.Empty(Report.Filter(rows, new ReportFilter { Q = "I26080219" }));
        // Delivery Date is blank on some rows; a range on it leaves those out.
        Assert.True(Report.Filter(rows, new ReportFilter { DateField = "Delivery", From = new(2026, 1, 1) }).Count < 100);
    }

    [Fact]
    public void Time_chart_groups_by_day_week_or_month()
    {
        var rows = Sample().Rows; // ETA 2026-09-01 (Tue), 09-04 (Fri), 09-07 (Mon)
        var auto = Report.Summarize(rows, "ETA");
        Assert.Equal("Day", auto.Group); // short span → days
        Assert.Equal(3, auto.Daily.Labels.Count);

        var week = Report.Summarize(rows, "ETA", "Week");
        Assert.Equal(2, week.Daily.Labels.Count); // Mon 08-31 week and Mon 09-07 week
        Assert.Equal(["2026-08-31", "2026-09-06"], week.Daily.Ranges![0]);
        Assert.Equal(auto.Kpis.Containers, (int)week.Daily.Series.Sum(s => s.Values.Sum()));

        var month = Report.Summarize(rows, "ETA", "Month");
        Assert.Equal(["Sep 2026"], month.Daily.Labels);
        Assert.Equal(["2026-09-01", "2026-09-30"], month.Daily.Ranges![0]);
    }

    [Fact]
    public void Summary_counts_each_invoice_once_for_cbm()
    {
        var rows = Report.Filter(Sample().Rows, new ReportFilter { Q = "JLB3A746574637-0" });
        var s = Report.Summarize(rows, "ETA");
        Assert.Equal(1, s.Kpis.Invoices);
        Assert.Equal(15, s.Kpis.Containers);
        Assert.Equal(1112.66, s.Kpis.Cbm);
        Assert.Equal(1, s.Kpis.RevisedInvoices);
    }
}
