using System.ComponentModel.DataAnnotations.Schema;

namespace Modulo.GestionDistribucionGastos.Models;

[Table("Distribucion_Final")]
public class DistribucionFinal
{
    public int Id { get; set; }
    public int StagingId { get; set; }
    public string? NroAsiento { get; set; }
    public int? LineaId { get; set; }
    public string AnioMes { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }

    public string? NroCuenta { get; set; }
    public string? NombreCuenta { get; set; }

    public string? CodCentroCostoOriginal { get; set; }
    public string? CentroCostoOriginal { get; set; }
    public string? CodCanalOriginal { get; set; }
    public string? CanalOriginal { get; set; }
    public string? CodSucursalOriginal { get; set; }
    public string? SucursalOriginal { get; set; }
    public decimal MontoOriginal { get; set; }

    public string? CodCentroCostoDestino { get; set; }
    public string? CentroCostoDestino { get; set; }
    public string? CodCanalDestino { get; set; }
    public string? CanalDestino { get; set; }
    public string? CodSucursalDestino { get; set; }
    public string? SucursalDestino { get; set; }
    public decimal MontoDistribuido { get; set; }

    public string TipoOrigen { get; set; } = "SIN_AJUSTE"; // SIN_AJUSTE | AUTO | MANUAL
    public string? ReglaAplicada { get; set; }
    public string? UsuarioResponsable { get; set; }
    public DateTime? FechaAsignacion { get; set; }
    public string? Comentario { get; set; }
    public string Estado { get; set; } = "PENDIENTE"; // PENDIENTE | EN_REVISION | APROBADO

    // Gasto no directo del canal (ej. costos compartidos/corporativos): se excluye del Resultado
    // Operacional 1 del EERR y recién se resta en el Resultado Operacional 2. Por defecto en Operacional 1.
    public bool EsGastoIndirecto { get; set; }
}
