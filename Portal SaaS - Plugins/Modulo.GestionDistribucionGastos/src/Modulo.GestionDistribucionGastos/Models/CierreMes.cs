using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Cierre_Mes")]
public class CierreMes
{
    [Column(Order = 0)]
    public string AnioMes { get; set; } = string.Empty;
    public string Estado { get; set; } = "ABIERTO"; // ABIERTO | EN_PROCESO | CERRADO
    public DateTime? FechaCarga { get; set; }
    public DateTime? FechaCierre { get; set; }
    public string? UsuarioCierre { get; set; }
}
