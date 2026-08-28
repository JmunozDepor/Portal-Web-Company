using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

/// <summary>
/// dbo.Geografia. La columna ImagenTerritorio (varbinary(max)) queda deliberadamente
/// fuera del modelo -- no hay requerimiento de subir/mostrar imagenes de territorio
/// desde este mantenedor, y omitir la propiedad hace que EF Core nunca la toque.
/// </summary>
[Table("Geografia")]
public class Geografia
{
    public int IDGeografia { get; set; }
    public string Region { get; set; } = string.Empty;
    public string RegionCorto { get; set; } = string.Empty;
    public string? GrupoRegion { get; set; }
    public string CodigoPais { get; set; } = string.Empty;
    public string Pais { get; set; } = string.Empty;
    public string? CodigoPostal { get; set; }
}
