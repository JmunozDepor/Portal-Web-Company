using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Agrupacion_Cuenta")]
public class AgrupacionCuenta
{
    public string NroCuenta { get; set; } = string.Empty;
    public string? NombreCuenta { get; set; }
    public int? ClasificacionId { get; set; }
    public DateTime FechaModificacion { get; set; } = DateTime.Now;
    public string? UsuarioModificacion { get; set; }
}
