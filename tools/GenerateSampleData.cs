#:package ClosedXML@0.105.1
// Makes a large fake shipment workbook in the same layout as "File for Test_Candidate.xlsx".
// Run: dotnet run tools/GenerateSampleData.cs -- [rows] [output path]
using ClosedXML.Excel;

int targetRows = args.Length > 0 ? int.Parse(args[0]) : 3000;
string output = args.Length > 1 ? args[1] : Path.Combine("samples", "shipments-large.xlsx");

var rnd = new Random(2026); // fixed seed: same file every run

string[] headers =
[
    "Job I", "ETA", "ATA", "Invoice No", "Origin(Port Name)", "Release Port", "Carrier Name", "CTNR NO.",
    "CTNR NO. Count", "Seal", "Container Size", "Cartons", "CBM", "FileNameDate", "DO Release",
    "Shipping Document receive (วันที่ได้เอกสารในการทำใบขน)", "Shipping Document issue reason (ข้อมูลผิดรอแก้ไข)",
    "Shipping Document Complete (วันที่ได้เอกสารพร้อมทำใบขน)", "Status Declaration",
    "Customs Relese Import (Declaration complete) (0209)", "Delivery Plan", "Import Decleration Relese (0309)",
    "Delivery Date (Customs)", "Delivery Place",
];
(string Port, string Country, int SailDays)[] origins =
[
    ("Haiphong", "VN", 6), ("Chittagong", "BD", 12), ("Sihanoukville", "KH", 4), ("Semarang", "ID", 7),
    ("PORT QASIM", "PK", 15), ("Colombo", "LK", 10), ("Shanghai", "CN", 9), ("Busan", "KR", 11),
];
string[] carriers = ["Maersk", "Maersk", "Maersk", "CNC LINE", "ONE", "Evergreen", "Hapag-Lloyd"];
string[] releasePorts = ["TEST-B1", "TEST-B1", "TEST-B3", "TEST-B5", "TEST-A0"];
string[] sizes = ["40H", "40H", "40H", "40S", "20S"];
string[] prefixes = ["TEST", "JLB3A", "ID87L", "KSFDK", "L98S3", "TESTYL", "4J8SS", "QX81V", "U38WE"];
const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

T Pick<T>(T[] a) => a[rnd.Next(a.Length)];
DateTime Work(DateTime d) => d.DayOfWeek switch { DayOfWeek.Saturday => d.AddDays(2), DayOfWeek.Sunday => d.AddDays(1), _ => d };
DateTime At(DateTime d) => d.Date.AddHours(rnd.Next(8, 20)).AddMinutes(rnd.Next(60)).AddSeconds(rnd.Next(60));
string Ctnr() => new string(Enumerable.Range(0, 4).Select(_ => Letters[rnd.Next(26)]).ToArray()) + rnd.Next(1000000, 9999999);

using var wb = new XLWorkbook();
var ws = wb.AddWorksheet("data_excel (large)");
for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
ws.Row(1).Style.Font.Bold = true;

int r = 2, job = 260100000, invoiceSeq = 746500000;
var start = new DateTime(2026, 1, 5);
while (r - 2 < targetRows)
{
    // One job = one vessel arrival; 1-3 invoices; each invoice ships in 1-12 containers.
    var origin = Pick(origins);
    var carrier = Pick(carriers);
    var eta = Work(start.AddDays(rnd.Next(0, 360)));
    var ata = eta.AddDays(rnd.NextDouble() < 0.8 ? 0 : rnd.Next(1, 3)); // 20% arrive late
    var releasePort = Pick(releasePorts);
    var jobNo = $"I{job++}";

    for (int inv = rnd.Next(1, 4); inv > 0 && r - 2 < targetRows; inv--)
    {
        var invoice = $"{Pick(prefixes)}{invoiceSeq += rnd.Next(1, 50)}-0";
        int containers = rnd.NextDouble() < 0.6 ? rnd.Next(1, 3) : rnd.Next(3, 13);
        int cartons = rnd.Next(200, 16000);
        double cbm = Math.Round(cartons * (0.02 + rnd.NextDouble() * 0.08), 3);
        if (rnd.NextDouble() < 0.03) cbm = 0; // a few invoices have no CBM, like the real file

        bool revised = rnd.NextDouble() < 0.45;
        var docReceive = At(eta.AddDays(-rnd.Next(8, 20)));
        var docRevised = revised ? At(docReceive.AddDays(rnd.Next(2, 15))) : docReceive;
        var docComplete = At((revised ? docRevised : docReceive).AddDays(rnd.Next(0, 4)));
        string status = rnd.NextDouble() < 0.6 ? "409" : "";
        var customs = Work(ata.AddDays(rnd.Next(origin.Port == "PORT QASIM" ? 4 : 2, revised ? 14 : 10)));
        DateTime? delivered = rnd.NextDouble() < 0.6 ? Work(customs.AddDays(rnd.Next(0, 3))) : null;
        var doRelease = Work(eta.AddDays(rnd.Next(0, 4)));
        string place = rnd.NextDouble() < 0.85 ? "BKK-WH" : "LCB-WH";
        string carrierText = carrier == "CNC LINE" && rnd.NextDouble() < 0.1 ? "CNC LINE." : carrier; // typo seen in real data

        for (int c = 0; c < containers && r - 2 < targetRows; c++, r++)
        {
            object?[] row =
            [
                jobNo, eta, ata, invoice, origin.Port, releasePort, carrierText, Ctnr(), 1,
                $"ML-{origin.Country}{rnd.Next(1000000, 9999999)}", Pick(sizes), cartons, cbm,
                eta.AddDays(rnd.Next(5, 30)).ToString("yyyyMMdd"), doRelease,
                $"{docReceive:yyyy-MM-dd HH:mm:ss} {docRevised:yyyy-MM-dd HH:mm:ss}", revised ? "Revised Document" : null,
                docComplete, status == "" ? null : 409, customs, customs, customs, delivered, place,
            ];
            for (int i = 0; i < row.Length; i++)
            {
                var cell = ws.Cell(r, i + 1);
                switch (row[i])
                {
                    case DateTime d:
                        cell.Value = d;
                        cell.Style.DateFormat.Format = d.TimeOfDay == TimeSpan.Zero ? "m/d/yyyy" : "m/d/yyyy h:mm";
                        break;
                    case int n: cell.Value = n; break;
                    case double n: cell.Value = n; break;
                    case string s: cell.Value = s; break;
                }
            }
        }
    }
}
int dataRows = r - 2;

// Messy rows the importer must cope with: a stray value and a blank row at the bottom.
ws.Cell(r + 1, 6).Value = "2q";

ws.Columns().AdjustToContents(1, 200);
ws.SheetView.FreezeRows(1);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
wb.SaveAs(output);
Console.WriteLine($"Wrote {dataRows} rows ({job - 260100000} jobs) to {Path.GetFullPath(output)}");
