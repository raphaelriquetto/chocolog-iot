using System.Globalization;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Cliente HTTP do FIWARE: 8 s de limite para não travar a tela quando a EC2 está desligada
builder.Services.AddHttpClient("fiware", c => c.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddScoped<FiwareClient>();

// Supervisão em segundo plano: médias x limites -> comando ao ESP32
builder.Services.AddSingleton<MonitorState>();
builder.Services.AddHostedService<AlertMonitor>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// Formulários sempre lidos no formato invariável (15.5), mesmo num Windows em português
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = [CultureInfo.InvariantCulture],
});

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
