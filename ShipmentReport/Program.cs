using ShipmentReport.Services;

// Free for individuals and companies under USD 1M revenue; larger companies need a QuestPDF Professional license.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
// Don't fail the whole PDF if a value has a character the font can't draw.
QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

// Fixed English formatting: on a Thai-locale server "yyyy" would otherwise print Buddhist years (2569).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<DatasetStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
