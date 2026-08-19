using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Maestro_Sucursal")]
public class MaestroSucursal
{
    public string CodSucursal { get; set; } = string.Empty;
    public string Sucursal { get; set; } = string.Empty;
    public string CodCanal { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    public string CodCentroCosto { get; set; } = string.Empty;
    public string CentroCosto { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}
