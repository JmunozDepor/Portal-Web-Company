using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("GrupoRetail")]
public class GrupoRetail
{
    public int IDGrupoRetail { get; set; }
    public string? NombreGrupoRetail { get; set; }
    public string? Propietario { get; set; }

    /// <summary>FK logica a Localizacion -- sin constraint declarado en la base.</summary>
    public int IDLocalizacion { get; set; }
}
