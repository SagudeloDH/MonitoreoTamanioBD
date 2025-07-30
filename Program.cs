using MonitoreoTamanioBD.Data;
using MonitoreoTamanioBD.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// habilitar puertos para conexi�n externa
builder.WebHost.ConfigureKestrel(opts =>
{
    // Puerto HTTPS abierto a todas las IPs
    opts.ListenAnyIP(7079, listen =>
    {
        listen.UseHttps();
    });
    // Puerto HTTP (sin TLS) tambi�n
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

// 4) <-- y tambi�n como hosted para el cron de 6AM
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<DatabaseMonitoringService>());

// 5) Blazor Server �cl�sico�
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

// �IMPORTANTE! A�ade UseRouting para habilitar endpoints:
app.UseRouting();

// 6) Mapea SignalR y la p�gina Razor Host
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();