namespace Modulo.Wms.Models;

public enum WmsTipoTransaccion { EnvioProducto, EnvioSucursal, EnvioOrdenes, EnvioIngresoAsn, ConfirmacionOrdenes, ConfirmacionIngreso }

public static class WmsTipoTransaccionInfo
{
    public static readonly Dictionary<WmsTipoTransaccion, string> Labels = new()
    {
        [WmsTipoTransaccion.EnvioProducto] = "Envío - Producto",
        [WmsTipoTransaccion.EnvioSucursal] = "Envío - Sucursal",
        [WmsTipoTransaccion.EnvioOrdenes] = "Envío - Órdenes",
        [WmsTipoTransaccion.EnvioIngresoAsn] = "Envío - Ingreso ASN",
        [WmsTipoTransaccion.ConfirmacionOrdenes] = "Confirmación Órdenes",
        [WmsTipoTransaccion.ConfirmacionIngreso] = "Confirmación Ingreso",
    };

    public static readonly Dictionary<WmsTipoTransaccion, string> ProcessorKeys = new()
    {
        [WmsTipoTransaccion.ConfirmacionOrdenes] = "Wms.SlshStageParser",
        [WmsTipoTransaccion.ConfirmacionIngreso] = "Wms.SvshStageParser",
    };
}

public class WmsDashboardResumen
{
    public int TotalTransacciones { get; set; }
    public int TotalOk { get; set; }
    public int TotalError { get; set; }
    public int TotalPendiente { get; set; }
    public List<WmsEstadisticaTipo> PorTipo { get; set; } = new();
}

public class WmsEstadisticaTipo
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string Label => WmsTipoTransaccionInfo.Labels[Tipo];
    public int Ok { get; set; }
    public int Error { get; set; }
    public int Pendiente { get; set; }
    public int Total => Ok + Error + Pendiente;
    public int? ConfirmadoWms { get; set; }
    public DateTimeOffset? UltimaEjecucion { get; set; }
    public string? UltimoEstado { get; set; }
}
