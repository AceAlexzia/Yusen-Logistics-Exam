namespace ShipmentReport.Services;

/// <summary>A date column the user can filter by. Prefixes match the Excel header (case-insensitive).</summary>
public sealed record DateField(string Key, string Label, params string[] HeaderPrefixes)
{
    public static readonly DateField[] All =
    [
        new("ETA", "ETA", "eta"),
        new("ATA", "ATA", "ata"),
        new("DO", "DO Release", "do release"),
        new("DocReceive", "Doc Received", "shipping document receive"),
        new("DocComplete", "Doc Complete", "shipping document complete"),
        new("Customs", "Customs Release (0209)", "customs rel"),
        new("DeliveryPlan", "Delivery Plan", "delivery plan"),
        new("Declaration", "Declaration Release (0309)", "import dec"),
        new("Delivery", "Delivery Date", "delivery date"),
    ];

    public static DateField Get(string? key) =>
        All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public sealed class ShipmentRow
{
    /// <summary>Original cell values in sheet column order (dates already converted to DateTime).</summary>
    public required object?[] Cells { get; init; }
    public string Job { get; init; } = "";
    public string Invoice { get; init; } = "";
    public string Po { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Carrier { get; init; } = "";
    public string Container { get; init; } = "";
    public string Size { get; init; } = "";
    public double? Cartons { get; init; }
    public double? Cbm { get; init; }
    public bool DocRevised { get; init; }
    public string Declaration { get; init; } = "";
    public Dictionary<string, DateTime?> Dates { get; init; } = [];

    public DateTime? Date(string key) => Dates.GetValueOrDefault(key);
}

public sealed class Dataset
{
    public required string FileName { get; init; }
    public required string SheetName { get; init; }
    public required List<string> Headers { get; init; }
    /// <summary>Column indexes (into Headers/Cells) that hold dates, for export formatting.</summary>
    public required HashSet<int> DateColumns { get; init; }
    public required List<ShipmentRow> Rows { get; init; }
    public int SkippedRows { get; init; }
    /// <summary>Date fields that exist in this file (others are hidden from the dropdown).</summary>
    public required List<DateField> DateFields { get; init; }
}
