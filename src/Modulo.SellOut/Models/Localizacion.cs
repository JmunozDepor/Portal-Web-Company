using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("Localizacion")]
public class Localizacion
{
    public int IDLocalizacion { get; set; }
    public string? Ciudad { get; set; }
    public string? CodigoComuna { get; set; }
    public string? Comuna { get; set; }

    /// <summary>FK logica a Geografia -- sin constraint declarado en la base, se resuelve
    /// en la UI via un &lt;select&gt; cargado desde IGeografiaRepositorio.</summary>
    public int? IDGeografia { get; set; }
}
