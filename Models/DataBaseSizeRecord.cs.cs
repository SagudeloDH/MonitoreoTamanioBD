using System.ComponentModel.DataAnnotations.Schema;

namespace MonitoreoTamanioBD.Models
{
    [Table("Auditoria_TamanoBD")]
    public class DatabaseSizeRecord
    {
        public int Id { get; set; }
        public DateTime FechaRegistro { get; set; }
        public string Servidor { get; set; }
        public string NombreBD { get; set; }
        public decimal TamanoMB { get; set; }
    }
}
