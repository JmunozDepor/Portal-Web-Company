using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.SellOut.Models;

/// <summary>
/// dbo.CfgClienteLayoutInput -- una fila por (IdCliente, TipoArchivo), consumida por el
/// pipeline SSIS externo para interpretar cada archivo de cada cliente retail. Cada
/// columna Map* es texto libre con una de tres formas (ver Pages/Layouts/Index.cshtml.cs
/// para el detalle de validacion):
///   1. Referencia a una columna de staging ("Col2", "Col17", ...).
///   2. Una expresion SQL de solo lectura sobre esas columnas
///      (ej. "TRY_CAST(Col22 AS NUMERIC(18,0)) / 1.19").
///   3. El literal sentinela "Calcular" -- el servicio de importacion calcula ese campo.
/// Este plugin NUNCA ejecuta ese texto -- solo lo persiste. El SSIS externo es quien lo
/// interpreta/ejecuta.
/// </summary>
[Table("CfgClienteLayoutInput")]
public class CfgClienteLayoutInput
{
    public int IdCliente { get; set; }
    public string TipoArchivo { get; set; } = string.Empty;

    public string Cliente { get; set; } = string.Empty;
    public bool? Activo { get; set; }
    public string FilePattern { get; set; } = string.Empty;

    public string? MapFecha { get; set; }
    public string? MapSku { get; set; }
    public string? MapDescripcionCliente { get; set; }
    public string? MapPrcCostoUnit { get; set; }
    public string? MapPrcVtaUnit { get; set; }
    public string? MapPrcVtaFull { get; set; }
    public string? MapBarcode { get; set; }
    public string? MapDepartamento { get; set; }
    public string? MapDefinicion1 { get; set; }
    public string? MapDefinicion2 { get; set; }
    public string? MapIdSucursal { get; set; }
    public string? MapSucursal { get; set; }
    public string? MapVtaUN { get; set; }
    public string? MapVtaNeta { get; set; }
    public string? MapVtaBruta { get; set; }
    public string? MapVtaBrutaFull { get; set; }
    public string? MapVtaCosto { get; set; }
    public string? MapStkUN { get; set; }
    public string? MapStkVtaNeta { get; set; }
    public string? MapStkVtaBruta { get; set; }
    public string? MapStkCosto { get; set; }

    /// <summary>char(1) en la base ('Y'/'N'), default 'N' -- se mapea como string de un
    /// caracter en vez de bool para no perder el default real de la columna.</summary>
    public string? CalcSkuBarcode { get; set; } = "N";
}
