using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("Cliente")]
public class Cliente
{
    public int IdCliente { get; set; }
    public string? NomCliente { get; set; }
    public bool? Activo { get; set; }
    public string? ClienteCodigo { get; set; }
    public DateTime? UpdateDate { get; set; }
    public int? OrdenReporte { get; set; }
    public int? PrimerSem_Inicio { get; set; }
    public int? PrimerSem_Fin { get; set; }
    public int? SegundoSem_Inicio { get; set; }
    public int? SegundoSem_Fin { get; set; }
    public string? Periodo { get; set; }
}
