using MonitoreoTamanioBD.Data;
using MonitoreoTamanioBD.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// habilitar puertos para conexión externa
builder.WebHost.ConfigureKestrel(opts =>
{
    // Puerto HTTPS abierto a todas las IPs
    opts.ListenAnyIP(7079, listen =>
    {
        listen.UseHttps();
    });
    // Puerto HTTP (sin TLS) también
    opts.ListenAnyIP(5079);
});

// 1) EF Core
builder.Services.AddDbContext<AuditoriaContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Auditoria")));

// 2) HttpClient para enviar alertas por WhatsApp
builder.Services.AddHttpClient();

// 3) Servicio de monitoreo + su interfaz + hosted service
builder.Services.AddSingleton<DatabaseMonitoringService>();
builder.Services.AddSingleton<IDatabaseMonitor>(sp =>
    sp.GetRequiredService<DatabaseMonitoringService>());

// 4) <-- y también como hosted para el cron de 6AM
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<DatabaseMonitoringService>());

// 5) Blazor Server “clásico”
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// ¡IMPORTANTE! Añade UseRouting para habilitar endpoints:
app.UseRouting();

// 6) Mapea SignalR y la página Razor Host
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();