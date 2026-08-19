using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Staging_CentralizacionContable")]
public class StagingCentralizacion
{
    public int Id { get; set; }
    public string? CodigoGrupo { get; set; }
    public string? NombreGrupo { get; set; }
    public string? NroCuenta { get; set; }
    public string? NombreCuenta { get; set; }
    public string? CodigoSocio { get; set; }
    public string? SocioNegocio { get; set; }
    public string? CodCentroCosto { get; set; }
    public string? CentroCosto { get; set; }
    public string? CodMarca { get; set; }
    public string? Marca { get; set; }
    public string? CodCanal { get; set; }
    public string? Canal { get; set; }
    public string? CodSucursal { get; set; }
    public string? Sucursal { get; set; }
    public string? CodTipoGasto { get; set; }
    public string? TipoGasto { get; set; }
    public string? NroAsiento { get; set; }
    public int? LineaId { get; set; }
    public string? DocInternoSAP { get; set; }
    public DateTime? Fecha { get; set; }
    public string? Comentarios { get; set; }
    public decimal Debito { get; set; }
    public decimal Credito { get; set; }
    public decimal MontoNeto { get; set; }
    public string AnioMes { get; set; } = string.Empty;
    public string TipoRegistro { get; set; } = "DETALLE"; // DETALLE | RESUMEN
    public DateTime FechaCarga { get; set; }
}
