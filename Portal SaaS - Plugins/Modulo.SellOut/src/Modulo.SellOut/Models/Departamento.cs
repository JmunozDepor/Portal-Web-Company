using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("Departamento")]
public class Departamento
{
    public int IdCliente { get; set; }
    public string ClienteDepartamento { get; set; } = string.Empty;
    public string? DescripcionDeptoLocal { get; set; }
    public DateTime? UpdateDate { get; set; }
}
