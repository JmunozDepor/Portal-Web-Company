namespace Modulo.GestionDistribucionGastos.Models;

public class DashboardViewModel
{
    public string AnioMes { get; set; } = string.Empty;
    public string EstadoMes { get; set; } = "ABIERTO";
    public int LineasTotales { get; set; }
    public int ResueltasAuto { get; set; }
    public int ResueltasManual { get; set; }
    public int Pendientes { get; set; }
}

public class CuentaPendienteViewModel
{
    public string NroCuenta { get; set; } = string.Empty;
    public string? NombreCuenta { get; set; }
    public int LineasTotales { get; set; }
    public int LineasPendientes { get; set; }
    public decimal MontoPendiente { get; set; }
    public bool TomadaPorOtroUsuario { get; set; }
    public string? UsuarioQueLaTiene { get; set; }

    // Cuántas de las LineasTotales ya están marcadas como gasto indirecto -- para cuentas con miles
    // de líneas, marcar una por una en Detalle no es viable, así que además del toggle unitario ahí
    // existe este toggle global por cuenta (ver CuentasPendientes/Index.cshtml.cs.MarcarIndirectoCuenta).
    public int LineasIndirectas { get; set; }

    // Bloqueo duro post-aprobación (Cuenta_Aprobada) -- distinto del bloqueo transitorio de edición
    // (TomadaPorOtroUsuario/UsuarioQueLaTiene, que expira solo a los 15 minutos).
    public bool Aprobada { get; set; }
    public string? UsuarioAprobador { get; set; }
    public DateTime? FechaAprobacion { get; set; }
}

public class AsignacionMasivaInput
{
    public List<LineaAsignacionInput> Lineas { get; set; } = new();
    public string Comentario { get; set; } = string.Empty;
}

public class LineaAsignacionInput
{
    public int Id { get; set; }
    public string? CodCentroCosto { get; set; }
    public string? CodCanal { get; set; }
    public string? CodSucursal { get; set; }
    public bool EsGastoIndirecto { get; set; }
}

// Input de la pantalla "Dividir línea" (CuentasPendientes/Detalle): reparte manualmente el
// monto de una línea de Staging en N partidas con destino y monto propios, sin depender de
// ninguna Regla_Distribucion/base de venta (ver DetalleModel.OnPostDividirLineaAsync).
public class DivisionLineaInput
{
    public int StagingId { get; set; }
    public List<PartidaDivisionInput> Partidas { get; set; } = new();
    public string? Comentario { get; set; }
    public bool EsGastoIndirecto { get; set; }
}

public class PartidaDivisionInput
{
    public string? CodCentroCosto { get; set; }
    public string? CodCanal { get; set; }
    public string? CodSucursal { get; set; }
    public decimal Monto { get; set; }
}

public class ReporteFilaViewModel
{
    public string? NroCuenta { get; set; }
    public string? NombreCuenta { get; set; }
    public string? CanalDestino { get; set; }
    public string? SucursalDestino { get; set; }
    public string? CentroCostoDestino { get; set; }
    public decimal MontoOriginal { get; set; }
    public decimal MontoDistribuido { get; set; }
    public string TipoOrigen { get; set; } = string.Empty;
}

// Resumen tipo EERR anual (grupos 4-9), filas = grupo contable, columnas = canal (o sucursal si se
// filtra un canal específico, simulando el "drill down" del pivot de Excel que sirvió de referencia).
public class EerrViewModel
{
    public int Anio { get; set; }
    public List<int> AniosDisponibles { get; set; } = new();
    public string? Canal { get; set; }
    public string? Sucursal { get; set; }
    public string? CentroCosto { get; set; }
    public List<(string Codigo, string Nombre)> Canales { get; set; } = new();
    public List<(string Codigo, string Nombre)> Sucursales { get; set; } = new();
    public List<(string Codigo, string Nombre)> CentrosCosto { get; set; } = new();

    public bool ColumnasSonSucursal { get; set; }
    public List<string> Columnas { get; set; } = new();
    public List<string> Grupos { get; set; } = new();
    public Dictionary<string, Dictionary<string, decimal>> Celdas { get; set; } = new();
    public Dictionary<string, decimal> TotalesColumna { get; set; } = new();
    public Dictionary<string, decimal> TotalesFila { get; set; } = new();
    public decimal TotalGeneral { get; set; }

    // Estado de resultados propiamente dicho: Ingresos - Costo - Gastos (con el signo contable real de
    // cada grupo, no los valores en valor absoluto que se muestran en la grilla de arriba). Los gastos
    // (grupos 6-9) se dividen en directos del canal / indirectos según la marca EsGastoIndirecto de cada
    // línea (CuentasPendientes/Detalle), para separar Resultado Operacional 1 (solo directos) de 2 (todos).
    public decimal Ingresos { get; set; }
    public decimal CostoVentas { get; set; }
    public decimal UtilidadBruta { get; set; }
    public decimal GastosDirectos { get; set; }
    public decimal ResultadoOperacional1 { get; set; }
    public decimal GastosIndirectos { get; set; }
    public decimal ResultadoOperacional2 { get; set; }
    public decimal ResultadoFinal { get; set; }
}

// Pantalla Reglas: cada fila de la grilla + cuántas cuentas reales matchea el patrón de esa regla
// (NroCuenta ahora puede ser un patrón LIKE, ej. "61-09-%", no solo una cuenta exacta).
public class ReglaResumenViewModel
{
    public List<ReglaListItemViewModel> Reglas { get; set; } = new();
    public int TotalReglas { get; set; }
    public int ReglasActivas { get; set; }
    public int ReglasInactivas { get; set; }
}

public class ReglaListItemViewModel
{
    public ReglaDistribucion Regla { get; set; } = null!;
    public int CuentasQueMatchea { get; set; }
}
