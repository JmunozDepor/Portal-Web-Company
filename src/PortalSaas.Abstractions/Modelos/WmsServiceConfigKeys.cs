namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Whitelist fija de claves de configuración que WmsSapIntegration.Service (standalone,
/// ver ARQUITECTURA.md de Modulo.Wms) acepta desde wms_oracle_service_configs, solo si
/// esa instancia tiene "ConfigSource": "Database" en su propio appsettings.json. Es un
/// contrato manual de strings entre ese servicio y este módulo -- no hay proyecto
/// compartido entre las dos soluciones. Portado tal cual desde
/// WMS_Suite/WmsPortal.Core/Models/ServiceConfigModels.cs (ServiceConfigKeys.Labels).
///
/// Deliberadamente NO incluye: credenciales (SapSettings:UserName/Password,
/// WmsIntegration:User/Pass -- quedan solo en env/vault del servidor, nunca en una
/// tabla editable desde la web) ni WmsSettings:* (rutas de filesystem del host donde
/// corre el servicio, no de la empresa).
/// </summary>
public static class WmsServiceConfigKeys
{
    public static readonly IReadOnlyDictionary<string, (string Label, string Hint)> Labels = new Dictionary<string, (string, string)>
    {
        ["HostedServices:Wms_FileWatcher"] = ("Ingesta legacy por archivo (SFTP)", "true / false"),
        ["HostedServices:IHTH_Processor"] = ("Aplanado IHTH", "true / false"),
        ["HostedServices:SLSH_Processor"] = ("Aplanado SLSH", "true / false"),
        ["HostedServices:SVSH_Processor"] = ("Aplanado SVSH", "true / false"),
        ["HostedServices:WmsInbound_IHTHConfirmProcessor"] = ("Confirmación IHTH → SAP", "true / false"),
        ["HostedServices:WmsInbound_OrderConfirmProcessor"] = ("Confirmación Órdenes → SAP", "true / false"),
        ["HostedServices:WmsInbound_ReceipConfirmProcessor"] = ("Confirmación Ingresos → SAP", "true / false"),
        ["HostedServices:WmsOutbound_ItemBarcodeProcessor"] = ("Envío Códigos de Barra → WMS", "true / false"),
        ["HostedServices:WmsOutbound_ItemProcessor"] = ("Envío Productos → WMS", "true / false"),
        ["HostedServices:WmsOutbound_OrderProcessor"] = ("Envío Órdenes → WMS", "true / false"),
        ["HostedServices:WmsOutbound_ShipmentProcessor"] = ("Envío Ingresos ASN → WMS", "true / false"),
        ["HostedServices:WmsOutbound_StoreProcessor"] = ("Envío Sucursales → WMS", "true / false"),
        ["HostedServices:WmsOutbound_StageErrorProcessor"] = ("Detección de rechazos WMS (stage_*)", "true / false"),
        ["HostedServices:WmsOutbound_ExistsProcessor"] = ("Confirmación de llegada a WMS (entidad final)", "true / false"),
        ["WmsIntegration:BatchSize"] = ("Tamaño de lote por ciclo", "número entero"),
        ["WmsIntegration:InboundIntervalSeconds"] = ("Intervalo confirmaciones WMS→SAP (segundos)", "número entero"),
        ["WmsIntegration:OutboundIntervalSeconds"] = ("Intervalo envío de maestros SAP→WMS (segundos)", "número entero"),
        ["WmsIntegration:MasterDataIntervalSeconds"] = ("Intervalo datos maestros (segundos)", "número entero"),
        ["WmsIntegration:RetentionDays"] = ("Días de retención en tablas de staging", "número entero"),
        ["WmsIntegration:MaxParallelism"] = ("Hilos en paralelo (Parallel.ForEachAsync)", "número entero"),
        ["WmsIntegration:SaveLocalXml"] = ("Guardar XML/JSON local para diagnóstico", "true / false"),
        ["WmsIntegration:ApiUrl"] = ("URL API Oracle WMS", "texto (URL)"),
        ["WmsIntegration:EnvCode"] = ("Código de ambiente WMS", "texto"),
        ["WmsIntegration:ParentCompany"] = ("Empresa padre WMS", "texto"),
        ["WmsIntegration:LgfApiBaseUrl"] = ("URL base LGFAPI de WMS", "texto (URL)"),
        ["WmsIntegration:StageErrorIntervalSeconds"] = ("Intervalo detección de rechazos WMS (segundos)", "número entero"),
        ["WmsIntegration:ExistsIntervalSeconds"] = ("Intervalo confirmación de llegada a WMS (segundos)", "número entero"),
        ["WmsIntegration:ValidationBatchSize"] = ("Tamaño de lote de validación WMS", "número entero"),
        ["WmsIntegration:ExistsDiasHaciaAtras"] = ("Ventana de días para confirmar Órdenes/ASN en WMS (no aplica a Productos)", "número entero"),
        ["SapSettings:ServiceLayerUrl"] = ("URL SAP Service Layer", "texto (URL)"),
        ["SapSettings:CompanyDB"] = ("Base de datos SAP (CompanyDB)", "texto"),
        ["Logging:ReplicateToDatabase"] = ("Replicar log a base de datos (INT_SERVICE_LOG)", "true / false"),
        ["Logging:RetentionDays"] = ("Días de retención en INT_SERVICE_LOG", "número entero"),
    };
}
