namespace ShipmentReport.Services;

/// <summary>Bound from the query string / form. Empty values mean "no filter".</summary>
public sealed class ReportFilter
{
    public string DateField { get; set; } = "ETA";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>Exact Job, Invoice No or PO (case ignored).</summary>
    public string? Q { get; set; }
    // Set by clicking a chart.
    public string? Origin { get; set; }
    public string? Carrier { get; set; }
    public string? Size { get; set; }
    /// <summary>"Revised" or "Clean".</summary>
    public string? Doc { get; set; }
    /// <summary>A declaration status, or "None" for blank.</summary>
    public string? Decl { get; set; }
    public string? Release { get; set; }
    /// <summary>A <see cref="Report.DeliveryStatuses"/> value.</summary>
    public string? Deliv { get; set; }
    /// <summary>Time chart buckets: "Day", "Week" or "Month"; empty = pick from the date span.</summary>
    public string? Group { get; set; }

    public IEnumerable<(string Label, string Value)> Describe()
    {
        var label = Services.DateField.Get(DateField).Label;
        if (From is not null || To is not null)
            yield return (label, $"{From?.ToString("yyyy-MM-dd") ?? "…"} to {To?.ToString("yyyy-MM-dd") ?? "…"}");
        if (!string.IsNullOrWhiteSpace(Q)) yield return ("PO / Invoice / Job", Q.Trim());
        if (Origin is not null) yield return ("Origin", Origin);
        if (Carrier is not null) yield return ("Carrier", Carrier);
        if (Size is not null) yield return ("Container size", Size);
        if (Doc is not null) yield return ("Documents", Doc);
        if (Decl is not null) yield return ("Declaration", Decl);
        if (Release is not null) yield return ("Release port", Release);
        if (Deliv is not null) yield return ("Delivery", Deliv);
    }
}

public sealed record Kpis(int Rows, int Jobs, int Invoices, int Containers, double Cbm, double Cartons,
    double? AvgDaysToCustoms, double? AvgDaysToDelivery, int RevisedInvoices, int MissingDate,
    int OnTimeContainers, int LateContainers, int NotDeliveredContainers)
{
    /// <summary>Share of delivered containers (with a plan) that arrived on or before the plan date.</summary>
    public double? OnTimePercent => OnTimeContainers + LateContainers == 0 ? null
        : Math.Round(100.0 * OnTimeContainers / (OnTimeContainers + LateContainers), 1);
}

public sealed record Series(string Label, List<double> Values);
/// <param name="Ranges">For time charts: each bar's [from, to] dates (yyyy-MM-dd), used by click-to-filter.</param>
public sealed record ChartData(List<string> Labels, List<Series> Series, List<int>? Counts = null, List<string[]>? Ranges = null);

/// <param name="Group">The bucket the time chart actually used: "Day", "Week" or "Month".</param>
public sealed record ReportSummary(Kpis Kpis, string Group, ChartData Daily, ChartData ByOrigin, ChartData ByCarrier,
    ChartData LeadTime, ChartData DocStatus, ChartData Declaration, ChartData ByReleasePort, ChartData Delivery);

public static class Report
{
    public static List<ShipmentRow> Filter(IEnumerable<ShipmentRow> rows, ReportFilter f)
    {
        var key = DateField.Get(f.DateField).Key;
        var q = f.Q?.Trim();
        return rows.Where(r =>
        {
            if (f.From is not null || f.To is not null)
            {
                if (r.Date(key) is not { } d) return false;
                var day = DateOnly.FromDateTime(d);
                if (day < f.From || day > f.To) return false;
            }
            if (!string.IsNullOrEmpty(q) && !(Is(r.Job, q) || Is(r.Invoice, q) || Is(r.Po, q))) return false;
            if (f.Origin is not null && r.Origin != f.Origin) return false;
            if (f.Carrier is not null && r.Carrier != f.Carrier) return false;
            if (f.Size is not null && r.Size != f.Size) return false;
            if (f.Doc is not null && r.DocRevised != (f.Doc == "Revised")) return false;
            if (f.Decl is not null && DeclLabel(r) != f.Decl) return false;
            if (f.Release is not null && (r.ReleasePort == "" ? "Unknown" : r.ReleasePort) != f.Release) return false;
            if (f.Deliv is not null && DeliveryStatus(r) != f.Deliv) return false;
            return true;
        }).ToList();

        // Whole number only (case ignored): typing part of a number matches nothing.
        static bool Is(string s, string q) => s != "" && s.Equals(q, StringComparison.OrdinalIgnoreCase);
    }

    public static string DeclLabel(ShipmentRow r) => r.Declaration == "" ? "None" : r.Declaration;

    public static readonly string[] DeliveryStatuses = ["On time", "Late", "Not delivered", "No plan"];

    /// <summary>Per container row: no Delivery Date yet, no Delivery Plan to compare with, or delivered on/after the plan day.</summary>
    public static string DeliveryStatus(ShipmentRow r) =>
        r.Date("Delivery") is not { } done ? "Not delivered"
        : r.Date("DeliveryPlan") is not { } plan ? "No plan"
        : done.Date <= plan.Date ? "On time" : "Late";

    static string ContainerKey(ShipmentRow r) => r.Container != "" ? r.Container : "row:" + r.GetHashCode();

    /// <summary>
    /// The sheet has one row per container per invoice, and an invoice's Cartons/CBM repeat on each of its rows,
    /// so CBM and per-invoice figures count each invoice once.
    /// </summary>
    static List<ShipmentRow> OnePerInvoice(IEnumerable<ShipmentRow> rows) =>
        rows.GroupBy(r => r.Invoice != "" ? r.Invoice : r.Job).Select(g => g.First()).ToList();

    static double? AvgDays(IEnumerable<ShipmentRow> invoices, string to)
    {
        var days = invoices.Where(r => r.Date("ETA") is not null && r.Date(to) is not null)
            .Select(r => (r.Date(to)!.Value.Date - r.Date("ETA")!.Value.Date).TotalDays).ToList();
        return days.Count == 0 ? null : Math.Round(days.Average(), 1);
    }

    public static readonly string[] Groups = ["Day", "Week", "Month"];

    /// <summary>Days for up to 2 months, weeks for up to ~6 months, months beyond that.</summary>
    public static string ResolveGroup(string? group, List<DateTime> days)
    {
        if (Groups.Contains(group)) return group!;
        var span = days.Count == 0 ? 0 : (days[^1] - days[0]).TotalDays;
        return span <= 62 ? "Day" : span <= 183 ? "Week" : "Month";
    }

    /// <summary>First and last day of the bucket a date falls in. Weeks run Monday–Sunday.</summary>
    static (DateTime From, DateTime To) Bucket(DateTime d, string group) => group switch
    {
        "Week" => (d.AddDays(-(((int)d.DayOfWeek + 6) % 7)), d.AddDays(-(((int)d.DayOfWeek + 6) % 7) + 6)),
        "Month" => (new DateTime(d.Year, d.Month, 1), new DateTime(d.Year, d.Month, 1).AddMonths(1).AddDays(-1)),
        _ => (d, d),
    };

    static string BucketLabel(DateTime from, string group) => group switch
    {
        "Week" => $"Wk {System.Globalization.ISOWeek.GetWeekOfYear(from)} ({from:dd MMM})",
        "Month" => from.ToString("MMM yyyy"),
        _ => from.ToString("yyyy-MM-dd"),
    };

    public static ReportSummary Summarize(List<ShipmentRow> rows, string dateField, string? group = null)
    {
        var key = DateField.Get(dateField).Key;
        var invoices = OnePerInvoice(rows);
        var byDelivery = rows.GroupBy(DeliveryStatus).ToDictionary(g => g.Key, g => g.Select(ContainerKey).Distinct().Count());
        int Deliv(string s) => byDelivery.GetValueOrDefault(s);

        var kpis = new Kpis(
            Rows: rows.Count,
            Jobs: rows.Select(r => r.Job).Where(s => s != "").Distinct().Count(),
            Invoices: invoices.Count,
            Containers: rows.Select(ContainerKey).Distinct().Count(),
            Cbm: Math.Round(invoices.Sum(r => r.Cbm ?? 0), 2),
            Cartons: invoices.Sum(r => r.Cartons ?? 0),
            AvgDaysToCustoms: AvgDays(invoices, "Customs"),
            AvgDaysToDelivery: AvgDays(invoices, "Delivery"),
            RevisedInvoices: invoices.Count(r => r.DocRevised),
            MissingDate: rows.Count(r => r.Date(key) is null),
            OnTimeContainers: Deliv("On time"),
            LateContainers: Deliv("Late"),
            NotDeliveredContainers: Deliv("Not delivered"));

        // 1. Containers per day / week / month, stacked by size.
        var dated = rows.Where(r => r.Date(key) is not null).ToList();
        var days = dated.Select(r => r.Date(key)!.Value.Date).Distinct().Order().ToList();
        var g = ResolveGroup(group, days);
        var buckets = days.Select(d => Bucket(d, g)).Distinct().ToList();
        var byBucket = dated.ToLookup(r => Bucket(r.Date(key)!.Value.Date, g));
        var sizes = rows.Select(r => r.Size == "" ? "Unknown" : r.Size).Distinct().Order().ToList();
        var daily = new ChartData(
            buckets.Select(b => BucketLabel(b.From, g)).ToList(),
            sizes.Select(s => new Series(s, buckets.Select(b => (double)byBucket[b]
                .Where(r => (r.Size == "" ? "Unknown" : r.Size) == s)
                .Select(ContainerKey).Distinct().Count()).ToList())).ToList(),
            Ranges: buckets.Select(b => new[] { b.From.ToString("yyyy-MM-dd"), b.To.ToString("yyyy-MM-dd") }).ToList());

        // 2. Containers and CBM by origin / carrier.
        ChartData By(Func<ShipmentRow, string> pick)
        {
            var groups = rows.GroupBy(r => pick(r) == "" ? "Unknown" : pick(r))
                .OrderByDescending(g => g.Select(ContainerKey).Distinct().Count()).ToList();
            return new ChartData(groups.Select(g => g.Key).ToList(),
            [
                new("Containers", groups.Select(g => (double)g.Select(ContainerKey).Distinct().Count()).ToList()),
                new("CBM", groups.Select(g => Math.Round(OnePerInvoice(g).Sum(r => r.Cbm ?? 0), 2)).ToList()),
            ]);
        }

        // 3. Lead time from ETA, by origin (per invoice; invoices missing the end date are left out).
        var leadGroups = invoices.GroupBy(r => r.Origin == "" ? "Unknown" : r.Origin).OrderBy(g => g.Key).ToList();
        var lead = new ChartData(leadGroups.Select(g => g.Key).ToList(),
        [
            new("ETA → Customs release", leadGroups.Select(g => AvgDays(g, "Customs") ?? 0).ToList()),
            new("ETA → Delivery", leadGroups.Select(g => AvgDays(g, "Delivery") ?? 0).ToList()),
        ], leadGroups.Select(g => g.Count()).ToList());

        // 4. Document / declaration status per invoice.
        var doc = new ChartData(["Revised", "Clean"],
            [new("Invoices", [invoices.Count(r => r.DocRevised), invoices.Count(r => !r.DocRevised)])]);
        var declGroups = invoices.GroupBy(DeclLabel).OrderBy(g => g.Key == "None").ThenBy(g => g.Key).ToList();
        var decl = new ChartData(declGroups.Select(g => g.Key).ToList(),
            [new("Invoices", declGroups.Select(g => (double)g.Count()).ToList())]);

        // 5. Delivery status per container, in a fixed order, leaving out statuses with none.
        var delivLabels = DeliveryStatuses.Where(byDelivery.ContainsKey).ToList();
        var delivery = new ChartData(delivLabels, [new("Containers", delivLabels.Select(s => (double)byDelivery[s]).ToList())]);

        return new ReportSummary(kpis, g, daily, By(r => r.Origin), By(r => r.Carrier), lead, doc, decl,
            By(r => r.ReleasePort), delivery);
    }
}
