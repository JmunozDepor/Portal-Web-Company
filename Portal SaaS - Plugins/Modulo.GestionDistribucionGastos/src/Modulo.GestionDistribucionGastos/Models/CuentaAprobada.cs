using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Cuenta_Aprobada")]
public class CuentaAprobada
{
    public string AnioMes { get; set; } = string.Empty;
    public string NroCuenta { get; set; } = string.Empty;
    public string UsuarioAprobador { get; set; } = string.Empty;
    public DateTime FechaAprobacion { get; set; } = DateTime.Now;
}
