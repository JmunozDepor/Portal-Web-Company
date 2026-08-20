using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

[Table("ClienteSku")]
public class ClienteSku
{
    public int IdCliente { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? DescripcionCliente { get; set; }
    public string? DeptoCliente { get; set; }
    public decimal? CostoUnit { get; set; }
    public decimal? VentaUnit { get; set; }
    public bool? Activo { get; set; }
    public string? Producto { get; set; }
    public decimal? PrecioFull { get; set; }
    public DateTime? UpdateDate { get; set; }
}
