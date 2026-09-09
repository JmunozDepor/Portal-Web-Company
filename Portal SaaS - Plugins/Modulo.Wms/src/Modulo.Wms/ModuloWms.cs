using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms;

/// <summary>
/// Punto de entrada del módulo de integración SAP Business One ↔ WMS Oracle Cloud
/// (antes comercializado como "Logfire" -- nunca usar ese nombre en código/UI/commits,
/// ver ARQUITECTURA.md). Fase 1: administración de mapeo de campos, configuración
/// operativa y heartbeat de WmsSapIntegration.Service (que sigue standalone, ver
/// ARQUITECTURA.md). Las páginas Razor del port de WmsPortal.Web se agregan en una
/// entrega posterior -- este archivo cablea el DbContext y el menú base primero.
/// </summary>
public sealed class ModuloWms : IModuloPortal
{
    public string ModuleCode => "Wms";
    public string Name => "Integración WMS";
    public string Version => "1.0.0";

    public bool ServesOwnWwwRoot => false;

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition
        {
            Code = "raiz",
            ParentCode = null,
            Name = "Integración WMS",
            Icon = "bi-truck",
            PageRoute = null,
            Order = 170,
        };

        yield return new MenuItemDefinition
        {
            Code = "dashboard",
            ParentCode = "raiz",
            Name = "Dashboard",
            Icon = "bi-speedometer2",
            PageRoute = "/wms/dashboard",
            Order = 0,
        };

        yield return new MenuItemDefinition
        {
            Code = "transacciones",
            ParentCode = "raiz",
            Name = "Transacciones",
            Icon = "bi-arrow-left-right",
            PageRoute = "/wms/transacciones",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "confirmaciones",
            ParentCode = "raiz",
            Name = "Confirmaciones",
            Icon = "bi-check2-circle",
            PageRoute = "/wms/confirmaciones",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "archivos-wms",
            ParentCode = "raiz",
            Name = "Archivos WMS",
            Icon = "bi-file-earmark-text",
            PageRoute = "/wms/archivos-wms",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "mapeo-campos",
            ParentCode = "raiz",
            Name = "Mapeo de Campos",
            Icon = "bi-diagram-2",
            PageRoute = "/wms/mapeo-campos",
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "campos-validacion",
            ParentCode = "raiz",
            Name = "Campos de Validación",
            Icon = "bi-check2-square",
            PageRoute = "/wms/campos-validacion",
            Order = 5,
        };

        yield return new MenuItemDefinition
        {
            Code = "ajustes-motor",
            ParentCode = "raiz",
            Name = "Ajustes del Motor",
            Icon = "bi-sliders",
            PageRoute = "/wms/ajustes-motor",
            Order = 6,
        };

        // "Configuración del Servicio" (/wms/configuracion-servicio) y su tabla
        // wms_oracle_service_configs fueron ELIMINADAS (2026-09-08): eran passthrough del
        // Windows Service externo WmsSapIntegration.Service (en retiro) y nada del Portal
        // las leía. Reemplazadas por /wms/ajustes-motor (wms_runtime_settings) para los
        // umbrales que sí usa este plugin. Ver ARQUITECTURA.md "Deuda de migración".

        yield return new MenuItemDefinition
        {
            Code = "estado-servicio",
            ParentCode = "raiz",
            Name = "Estado del Servicio",
            Icon = "bi-heart-pulse",
            PageRoute = "/wms/estado-servicio",
            Order = 7,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // WmsDbContext resuelto self-service vía IExternalDatabaseConnectionService --
        // mismo patrón exacto que ModuloRendiciones.RegisterServices. CompanyId
        // siempre obligatorio, sin fallback a Organization (regla dura del proyecto).
        services.AddDbContext<WmsDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.Wms requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }

            var connection = externalDb
                .ResolveConnectionAsync(ModuleCode, companyAccessor.CompanyId)
                .GetAwaiter().GetResult();

            switch (connection.EngineType)
            {
                case ExternalDatabaseEngineType.Postgres:
                    options.UseNpgsql(connection.ConnectionString);
                    break;
                case ExternalDatabaseEngineType.SqlServer:
                    options.UseSqlServer(connection.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
            }
        });

        services.AddMemoryCache();
        services.AddScoped<IFieldMappingService, FieldMappingService>();
        services.AddScoped<IValidationFieldService, ValidationFieldService>();
        services.AddScoped<IWmsRuntimeSettingsService, WmsRuntimeSettingsService>();
        services.AddScoped<IWmsInboundIngestionService, WmsInboundIngestionService>();
        services.AddScoped<IIntegrationEntityReader, WmsSlshInventoryReader>();
        services.AddScoped<IIntegrationEntityReader, WmsSapStageItemReader>();
        services.AddScoped<IIntegrationEntityReader, WmsSapStageStoreReader>();
        services.AddScoped<IIntegrationEntityReader, WmsSapStageInboundReader>();
        services.AddScoped<IIntegrationEntityReader, WmsSapStageOrderReader>();
        services.AddScoped<IIntegrationEntityReader, WmsSvshInventoryReader>();
        services.AddScoped<IIntegrationEntityWriter, WmsSapStageItemWriter>();
        services.AddScoped<IIntegrationEntityWriter, WmsSapStageStoreWriter>();
        services.AddScoped<IIntegrationEntityWriter, WmsSapStageInboundWriter>();
        services.AddScoped<IIntegrationEntityWriter, WmsSapStageOrderWriter>();
        services.AddScoped<IWmsServiceHeartbeatRecorder, WmsServiceHeartbeatRecorder>();
        services.AddScoped<IWmsDashboardService, WmsDashboardService>();
        services.AddScoped<IWmsTransaccionService, WmsTransaccionService>();
        services.AddScoped<IWmsConfirmacionService, WmsConfirmacionService>();
        services.AddScoped<IWmsArchivoService, WmsArchivoService>();
        services.AddHostedService<WmsSlshStageParser>();
        services.AddHostedService<WmsSvshStageParser>();
        services.AddHostedService<WmsStageErrorReconciler>();
        services.AddHostedService<WmsExistsReconciler>();

        // Conector de la etapa Subida (staging local -> Oracle WMS Cloud real). Se
        // agrega como OTRA registración de IIntegrationConnector -- el Host ya registra
        // SapDocumentConnector con AddScoped<IIntegrationConnector, ...> en Program.cs,
        // e IntegrationSyncHostedService resuelve TODOS los conectores vía
        // GetServices<IIntegrationConnector>() (no un único registro), así que no hay
        // conflicto entre este AddHttpClient y ese AddScoped. AddHttpClient (en vez de
        // "new HttpClient()" directo) gestiona el HttpClient vía IHttpClientFactory,
        // evita el socket exhaustion clásico de crear HttpClient a mano.
        services.AddHttpClient<IIntegrationConnector, WmsCloudConnector>();
        services.AddHttpClient<IWmsValidationApiClient, WmsValidationApiClient>();
    }
}
