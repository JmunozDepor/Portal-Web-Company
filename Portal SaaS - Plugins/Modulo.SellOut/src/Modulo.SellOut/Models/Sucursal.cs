using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("Sucursal")]
public class Sucursal
{
    public int IdCliente { get; set; }
    public int IdSucursal { get; set; }
    public string? NomSucursalCliente { get; set; }
    public string? NomSucursal { get; set; }
    public bool? Activo { get; set; }
    public string? Canal { get; set; }
    public string? SubCanal { get; set; }
    public string? Cadena { get; set; }
    public string? SubCadena { get; set; }
    public DateTime? UpdateDate { get; set; }

    /// <summary>FK logica a Localizacion -- sin constraint declarado en la base.</summary>
    public int? IDLocalizacion { get; set; }

    /// <summary>FK logica a GrupoRetail -- sin constraint declarado en la base.</summary>
    public int? IDGrupoRetail { get; set; }

    public string? SubCliente { get; set; }
    public string? GrupoSell { get; set; } = "Sell Out WHS";
    public string? Supervisor { get; set; }
}
