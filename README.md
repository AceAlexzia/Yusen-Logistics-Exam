# Import Shipment Report

A C# web app for import-shipment data. Import an `.xlsx` file, filter it by date range and PO / Invoice / Job
number, see interactive charts, and export the report to Excel or PDF.

Built with ASP.NET Core Razor Pages (.NET 10), [ClosedXML](https://github.com/ClosedXML/ClosedXML) for reading
and writing Excel, [QuestPDF](https://www.questpdf.com/) for PDF, and [Chart.js](https://www.chartjs.org/) for charts.

## Features

- **Import** – upload or drag in an `.xlsx`. The first sheet is read and columns are found by **header name**, not
  position. Excel date numbers become real dates. Empty or junk rows are skipped and reported.
- **Filters**
  - Date range on any date column (ETA, ATA, DO Release, Doc Received, Doc Complete, Customs Release,
    Delivery Plan, Declaration Release, Delivery Date), picked from a dropdown.
  - PO / Invoice / Job number: **exact match** (case ignored), with suggestions from the file while typing.
  - Click-to-filter on charts: a bar filters by its period / origin / carrier, a doughnut slice by status,
    and a size in the legend by container size. Active chart filters show as removable chips.
- **Summary figures** – jobs, invoices, containers, CBM, average days ETA → customs release and ETA → delivery,
  invoices with revised documents.
- **Charts** – containers per day / week / month (auto-picked from the date span) stacked by size; containers & CBM
  by origin and by carrier; lead time by origin; document and declaration status.
- **Export** – Excel (Summary sheet with filters, figures, chart tables and chart images, plus a Data sheet with
  every original column) and PDF (figures and charts on page 1, then the table). Both use the current filters.

## How the numbers are counted

The sheet has **one row per container per invoice**, and an invoice's Cartons / CBM repeat on each of its rows.
So:

- **Containers** = distinct `CTNR NO.`
- **CBM, Cartons, lead time, document / declaration status** are counted **once per invoice**.
- Lead time leaves out invoices whose end date is blank (e.g. no Delivery Date yet).

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet run --project ShipmentReport --launch-profile http
```

Open http://localhost:5162 and import `samples/shipments-large.xlsx` (3,000 fake rows) or
`ShipmentReport.Tests/TestData/sample.xlsx` (the 100-row exam file).

Tests:

```sh
dotnet test ShipmentReport.Tests
```

Make a bigger fake file (same layout, fixed random seed):

```sh
dotnet run tools/GenerateSampleData.cs -- 10000 samples/shipments-10k.xlsx
```

## Run with Docker

```sh
docker build -t shipment-report .
docker run -p 8080:8080 shipment-report
```

Then open http://localhost:8080. The container listens on `$PORT` when set (as on Render), else 8080.

## Project layout

| Path | What it is |
|---|---|
| `ShipmentReport/Services/ExcelImporter.cs` | Reads the workbook, matches columns by header, converts dates |
| `ShipmentReport/Services/Report.cs` | Filters, summary figures and chart data |
| `ShipmentReport/Services/ReportExporter.cs` | Excel and PDF export |
| `ShipmentReport/Pages/Index.cshtml(.cs)` | The page: upload, filters, figures, charts, table |
| `ShipmentReport/wwwroot/js/report.js` | Charts, click-to-filter, export of chart images, table sorting |
| `ShipmentReport.Tests/` | xUnit tests against the exam file |
| `tools/GenerateSampleData.cs` | Generates large fake test files |

## Notes and limits

- Uploaded files are kept **in server memory for 4 hours**; a restart clears them and the file must be imported again.
- The PDF uses QuestPDF's built-in Lato font, which has no Thai characters, so PDF column headers are short English
  names. The Excel export keeps the original (Thai) headers.
- QuestPDF is used under its Community license, which is free only for organizations under USD 1M annual revenue.
