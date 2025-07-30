namespace MonitoreoTamanioBD.Data
{
    using Microsoft.EntityFrameworkCore;
    using MonitoreoTamanioBD.Models;
    using System.Collections.Generic;

    public class AuditoriaContext : DbContext
    {
        public AuditoriaContext(DbContextOptions<AuditoriaContext> opt)
            : base(opt) { }

        public DbSet<DatabaseSizeRecord> Records { get; set; }
    }
}
