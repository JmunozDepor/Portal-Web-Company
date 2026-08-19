using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Reglas_Distribucion")]
public class ReglaDistribucion
{
    public int Id { get; set; }

    [Required, Display(Name = "Cuenta")]
    public string NroCuenta { get; set; } = string.Empty;

    [Display(Name = "Centro de costo (opcional)")]
    public string? CodCentroCosto { get; set; }

    [Required, Display(Name = "Base de distribución")]
    public string BaseDistribucion { get; set; } = "CANAL"; // CANAL | SUCURSAL

    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    public string? UsuarioCreacion { get; set; }

    [Display(Name = "Comentario")]
    public string? Comentario { get; set; }
}
