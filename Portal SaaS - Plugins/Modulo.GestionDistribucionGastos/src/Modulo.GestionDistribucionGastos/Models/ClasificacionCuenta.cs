using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Clasificacion_Cuenta")]
public class ClasificacionCuenta
{
    public int Id { get; set; }

    [Required, Display(Name = "Código")]
    public string Codigo { get; set; } = string.Empty;

    [Required, Display(Name = "Nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Display(Name = "Orden")]
    public int Orden { get; set; }

    public bool Activo { get; set; } = true;
}
