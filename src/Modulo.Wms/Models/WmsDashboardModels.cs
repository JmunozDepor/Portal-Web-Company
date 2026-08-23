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
    public int TotalEnviado { get; set; }
    public List<WmsEstadisticaTipo> PorTipo { get; set; } = new();
    public List<WmsProcesadorEstado> Procesadores { get; set; } = new();

    /// <summary>Items en Enviado que llevan >=15 intentos sin confirmación de Oracle (de 20 máximo,
    /// ver WmsExistsReconciler) -- a punto de caer a ErrorWms por timeout, no por un error real.</summary>
    public int ItemsEnviadosSinConfirmarProximosAFallar { get; set; }
}

public class WmsProcesadorEstado
{
    public string ProcessorKey { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

public class WmsEstadisticaTipo
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string Label => WmsTipoTransaccionInfo.Labels[Tipo];
    public int Ok { get; set; }
    public int Error { get; set; }

    /// <summary>Todavía no se envió a WMS (Status=Pendiente). No confundir con Enviado.</summary>
    public int Pendiente { get; set; }

    /// <summary>Se envió a WMS pero Oracle todavía no confirmó que lo procesó (Status=Enviado,
    /// ver WmsExistsReconciler). Antes se sumaba junto con Pendiente bajo un solo número
    /// "en curso" -- eso hacía invisible en el dashboard el momento en que un lote se envía
    /// (el total no se movía), así que se separan.</summary>
    public int Enviado { get; set; }

    public int Total => Ok + Error + Pendiente + Enviado;
    public int? ConfirmadoWms { get; set; }
    public DateTimeOffset? UltimaEjecucion { get; set; }
    public string? UltimoEstado { get; set; }
}
