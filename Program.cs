using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using MonitoreoTamanioBD.Data;
using MonitoreoTamanioBD.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor + Blazor
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(o => { o.DetailedErrors = true; }); // más pistas en consola

// DbContext (ponle timeout corto en dev)
builder.Services.AddDbContext<AuditoriaContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.CommandTimeout(10) // segundos
    ));

// HttpClient con timeout (WhatsApp)
builder.Services.AddHttpClient("WhatsApp", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

// HostedService: usa BackgroundService (ver #3)
builder.Services.AddHostedService<DatabaseMonitoringService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();   // NECESARIO para _framework/blazor.server.js
app.UseRouting();

app.MapBlazorHub();     // NECESARIO para SignalR/Blazor Server
app.MapFallbackToPage("/_Host");
app.MapGet("/health", () => Results.Ok("OK"));

app.Run();



////
//// 0) (Opcional) Kestrel escucha en todas las IPs
////
//builder.WebHost.ConfigureKestrel(opts =>
//{
//    opts.ListenAnyIP(7079, listen => listen.UseHttps());
//    opts.ListenAnyIP(5079);
//});

////
//// 1) EF Core
////
//builder.Services.AddDbContext<AuditoriaContext>(opt =>
//    opt.UseSqlServer(builder.Configuration.GetConnectionString("Auditoria")));

////
//// 2) HttpClient para WhatsApp
////
//builder.Services.AddHttpClient();

////
//// 3) Servicio de monitoreo + HostedService
////
//builder.Services.AddSingleton<DatabaseMonitoringService>();
//builder.Services.AddSingleton<IDatabaseMonitor>(sp =>
//    sp.GetRequiredService<DatabaseMonitoringService>());
//builder.Services.AddHostedService(sp =>
//    sp.GetRequiredService<DatabaseMonitoringService>());

////
//// 4) Blazor Server “clásico” (añade RazorPages + ServerSideBlazor)
////
//builder.Services.AddRazorPages();
//builder.Services.AddServerSideBlazor();

//var app = builder.Build();

////
//// 5) Middleware de errores / HSTS
////
//if (app.Environment.IsDevelopment())
//{
//    app.UseDeveloperExceptionPage();   // ¡muy útil para ver exceptions!
//}
//else
//{
//    app.UseExceptionHandler("/Error");
//    app.UseHsts();
//}

//app.UseHttpsRedirection();
//app.UseStaticFiles();

////
//// 6) Routing (obligatorio antes de MapRazorPages, MapBlazorHub…)
////
//app.UseRouting();

////
//// 7) ENDPOINTS
////
//app.MapRazorPages();                 // <–– Aquí registras tus páginas Razor (_Host.cshtml)
//app.MapBlazorHub();                  // Arranca SignalR para Blazor Server
//app.MapFallbackToPage("/_Host");     // Cualquier URL SPA va a Pages/_Host.cshtml

//app.Run();
