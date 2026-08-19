using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Cuenta_En_Trabajo")]
public class CuentaEnTrabajo
{
    public string AnioMes { get; set; } = string.Empty;
    public string NroCuenta { get; set; } = string.Empty;
    public string BloqueadoPor { get; set; } = string.Empty;
    public DateTime FechaBloqueo { get; set; } = DateTime.Now;
}
