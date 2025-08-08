using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MonitoreoTamanioBD.Data;
using MonitoreoTamanioBD.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MonitoreoTamanioBD.Services
{
    /// <summary>
    /// Servicio en segundo plano que:
    ///  - Ejecuta un chequeo diario a las 6AM (ExecuteAsync).
    ///  - Permite disparar manualmente la captura (MonitorOnceAsync).
    ///  
    /// Registra tamaño de datos y de log de las bases filtradas,
    /// inserta en AuditoriaDB y envía alertas por WhatsApp si crece >3%.
    /// </summary>
    public class DatabaseMonitoringService : BackgroundService, IDatabaseMonitor
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;

        /// <summary>
        /// Lista blanca de bases por cada cadena (Server1, Server2…)
        /// </summary>
        private static readonly Dictionary<string, List<string>> AllowedDatabases = new()
        {
            ["Server1"] = new List<string>
            {
                "Fichas",
                "integraciones_produccion",
                "Selene_Vtex",
                "Selene_App_Produccion",
                "Selene_WMS_Real",
                "Selene_Fichas",
                "UnoEE",
                "BI"
            },
            ["Server2"] = new List<string>
            {
                "CO_DTH_ADAPTER",
                "CO_DTH_BASE"
            },
            ["Server3"] = new List<string>
            {
                "db.e-BussinessINVOIC",
                "db.e-BussinessSuite",
                "DESADV",
                "SAC"
            }
        };

        /// <summary>
        /// Alias legibles para cada servidor (en lugar de su IP)
        /// </summary>
        private static readonly Dictionary<string, string> ServerAliases = new()
        {
            ["Server1"] = "Jupiter_AWS",   // 10.10.25.100
            ["Server2"] = "Copernico",      // 10.10.25.102
            ["Server3"] = "Copernico"      // 10.10.29.145
        };

        public DatabaseMonitoringService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            IHttpClientFactory httpFactory)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _httpFactory = httpFactory;
        }

        /// <summary>
        /// Lógica de scheduling: calcula el próximo disparo a las 6AM y ejecuta MonitorOnceAsync diario.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int runHour = _config.GetValue<int>("Monitor:Hour");
            int runMin = _config.GetValue<int>("Monitor:Minute");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var next = new DateTime(now.Year, now.Month, now.Day, runHour, runMin, 0);
                if (next <= now) next = next.AddDays(1);

                // Espera hasta la próxima 6AM
                await Task.Delay(next - now, stoppingToken);

                // Ejecuta la captura una vez
                await MonitorOnceAsync();

                // Duerme 24h para volver a las 6AM del día siguiente
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }

        /// <summary>
        /// Dispara manualmente la captura desde la UI (/runnow).
        /// </summary>
        public async Task MonitorOnceAsync()
        {
            // Nombramos las cadenas definidas en appsettings
            var connNames = new[] { "Server1", "Server2", "Server3" };

            foreach (var name in connNames)
            {
                // 1) Cadena y DataSource original
                var cs = _config.GetConnectionString(name);
                var servidor = new SqlConnectionStringBuilder(cs).DataSource;

                // 2) Sustituye la IP por alias si existe
                if (ServerAliases.TryGetValue(name, out var alias))
                {
                    servidor = alias;
                }

                // 3) Obtiene tamaños de data y log
                var sizes = await QueryDatabaseSizesAsync(cs);

                // 4) Filtra sólo las bases en la whitelist
                if (AllowedDatabases.TryGetValue(name, out var whitelist))
                {
                    sizes = sizes
                        .Where(t => whitelist.Contains(t.bd, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                // 5) Inserta en AuditoriaDB y envía alertas
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuditoriaContext>();

                foreach (var (bd, dataMb, logMb) in sizes)
                {
                    // 5.1) Registro de datos
                    db.Records.Add(new DatabaseSizeRecord
                    {
                        FechaRegistro = DateTime.Today,
                        Servidor = servidor,
                        NombreBD = $"{bd}_Data",
                        TamanoMB = dataMb
                    });

                    // 5.2) Registro de log
                    //db.Records.Add(new DatabaseSizeRecord
                    //{
                    //    FechaRegistro = DateTime.Today,
                    //    Servidor = servidor,
                    //    NombreBD = $"{bd}_Log",
                    //    TamanoMB = logMb
                    //});

                    // 5.3) Alerta si data crece >3%
                    var prevData = await db.Records
                        .Where(r =>
                            r.Servidor == servidor &&
                            r.NombreBD == $"{bd}_Data" &&
                            r.FechaRegistro < DateTime.Today)
                        .OrderByDescending(r => r.FechaRegistro)
                        .FirstOrDefaultAsync();

                    if (prevData != null && dataMb > prevData.TamanoMB * 1.03m)
                    {
                        await SendWhatsAppAlertAsync(servidor, $"{bd}_Data", prevData.TamanoMB, dataMb);
                    }

                    // 5.4) Alerta si log crece >3%
                    var prevLog = await db.Records
                        .Where(r =>
                            r.Servidor == servidor &&
                            r.NombreBD == $"{bd}_Log" &&
                            r.FechaRegistro < DateTime.Today)
                        .OrderByDescending(r => r.FechaRegistro)
                        .FirstOrDefaultAsync();

                    if (prevLog != null && logMb > prevLog.TamanoMB * 1.03m)
                    {
                        await SendWhatsAppAlertAsync(servidor, $"{bd}_Log", prevLog.TamanoMB, logMb);
                    }
                }

                // 6) Persiste los cambios en la base
                await db.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Consulta en sys.master_files los tamaños de Data y Log por database_id.
        /// </summary>
        private async Task<List<(string bd, decimal dataMb, decimal logMb)>> QueryDatabaseSizesAsync(string conn)
        {
            var list = new List<(string, decimal, decimal)>();
            using var cn = new SqlConnection(conn);
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                  DB_NAME(database_id) AS NombreBD,
                  SUM(CASE WHEN type_desc = 'ROWS' THEN size ELSE 0 END) * 8.0/1024 AS DataMB,
                  SUM(CASE WHEN type_desc = 'LOG'  THEN size ELSE 0 END) * 8.0/1024 AS LogMB
                FROM sys.master_files
                GROUP BY database_id;";

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                string bd = rdr.GetString(0);
                decimal data = rdr.GetDecimal(1);
                decimal log = rdr.GetDecimal(2);
                list.Add((bd, data, log));
            }

            return list;
        }

        /// <summary>
        /// Envía via HTTP petición a CallMeBot para mandar WhatsApp.
        /// </summary>
        private async Task SendWhatsAppAlertAsync(string server, string bd, decimal oldMB, decimal newMB)
        {
            var apiKey = _config["WhatsApp:ApiKey"];
            var phones = _config
                .GetSection("WhatsApp:Phones")
                .Get<string[]>() ?? Array.Empty<string>();

            // Formatea el texto de la alerta
            var text = Uri.EscapeDataString(
                $"¡Alerta! {bd} en {server} creció de {oldMB:F2}MB a {newMB:F2}MB (>3%).");

            var client = _httpFactory.CreateClient();
            foreach (var phone in phones)
            {
                var url = $"https://api.callmebot.com/whatsapp.php?" +
                          $"phone={phone}&text={text}&apikey={apiKey}";
                await client.GetAsync(url);
            }
        }
    }
}
