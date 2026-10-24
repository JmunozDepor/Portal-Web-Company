using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulo.Despachos.Data;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos;

/// <summary>
/// Punto de entrada del módulo de Despachos (manifiestos de despacho, retorno, tarifario
/// y cierre mensual de costos de transporte). Base de datos propia (motor dual, ver
/// Data/DespachosDbContext.cs); solo referencia PortalSaas.Abstractions.
/// </summary>
public sealed class ModuloDespachos : IModuloPortal
{
    public string ModuleCode => "Despachos";
    public string Name => "Despachos";

    /// <summary>Base externa propia (instancia + base) -- ver IModuloPortal.RequiresExternalDatabase.</summary>
    public bool RequiresExternalDatabase => true;
    public string Version => "1.0.0";

    // Trae wwwroot/css/despachos.css propio (badges de estado) y, más adelante, el lector
    // PDF417 vendorizado.
    public bool ServesOwnWwwRoot => true;

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return Group("raiz", null, "Despachos", "bi-truck", 170);

        yield return Group("grupo-operacion", "raiz", "Operación", "bi-truck-flatbed", 1);
        yield return Leaf("agrupar-traslados", "grupo-operacion", "Agrupar traslados", "bi-box-seam", "/despachos/agrupar-traslados", 1);
        yield return Leaf("manifiestos", "grupo-operacion", "Manifiestos", "bi-card-list", "/despachos/manifiestos", 2);
        yield return Leaf("retorno", "grupo-operacion", "Retorno", "bi-arrow-return-left", "/despachos/retorno", 3);
        yield return Leaf("reporte-documentos", "grupo-operacion", "Reporte de documentos", "bi-file-earmark-check", "/despachos/reportes/documentos", 4);

        yield return Group("grupo-costos", "raiz", "Costos", "bi-cash-coin", 2);
        yield return Leaf("tarifario", "grupo-costos", "Tarifario", "bi-tags", "/despachos/tarifario", 1);
        yield return Leaf("cierre-mensual", "grupo-costos", "Cierre mensual", "bi-calendar-check", "/despachos/cierre-mensual", 2);
        yield return Leaf("reportes", "grupo-costos", "Reporte mensual", "bi-bar-chart", "/despachos/reportes/mensual", 3);

        yield return Group("grupo-config", "raiz", "Configuración", "bi-gear", 3);
        yield return Config("transportistas", "Transportistas", "bi-building", 1);
        yield return Config("tipos-vehiculo", "Tipos de vehículo", "bi-truck-front", 2);
        yield return Config("vehiculos", "Vehículos", "bi-truck", 3);
        yield return Config("choferes", "Choferes", "bi-person-badge", 4);
        yield return Config("zonas", "Zonas", "bi-geo-alt", 5);
        yield return Config("origenes", "Orígenes", "bi-house-door", 6);
        yield return Config("turnos", "Turnos", "bi-clock", 7);
        yield return Config("motivos-rechazo", "Motivos de rechazo", "bi-x-octagon", 8);
        yield return Config("feriados", "Feriados", "bi-calendar-x", 9);
        yield return Config("destinatarios-avisos", "Destinatarios de avisos", "bi-bell", 10);
        yield return Config("consultas-sap", "Consultas SAP", "bi-database-gear", 11);
        yield return Config("equivalencias-comuna", "Equivalencias de comuna", "bi-signpost-split", 12);
        yield return Config("bodegas-sucursal", "Bodegas sucursal", "bi-shop", 13);
    }

    private static MenuItemDefinition Group(string code, string? parent, string name, string icon, int order) =>
        new() { Code = code, ParentCode = parent, Name = name, Icon = icon, PageRoute = null, Order = order };

    private static MenuItemDefinition Leaf(string code, string parent, string name, string icon, string route, int order) =>
        new() { Code = code, ParentCode = parent, Name = name, Icon = icon, PageRoute = route, Order = order };

    private static MenuItemDefinition Config(string code, string name, string icon, int order) =>
        Leaf(code, "grupo-config", name, icon, "/despachos/configuracion/" + code, order);

    public void RegisterServices(IServiceCollection services)
    {
        // DespachosDbContext resuelto self-service vía IExternalDatabaseConnectionService --
        // motor dual, se decide EN RUNTIME según ModuleExternalConnection.EngineType.
        // CompanyId SIEMPRE obligatorio (sin fallback a nivel Organization): si el usuario
        // todavía no eligió Company en la sesión, se rechaza acá con un mensaje claro.
        services.AddScoped<Servicios.IMasterDataService, Servicios.MasterDataService>();
        services.AddScoped<Servicios.ITariffService, Servicios.TariffService>();
        services.AddScoped<Servicios.ISapDocumentLookupService, Servicios.SapDocumentLookupService>();
        services.AddScoped<Servicios.IMonthlyClosureService, Servicios.MonthlyClosureService>();
        services.AddScoped<Servicios.IDispatchReportService, Servicios.DispatchReportService>();
        services.AddScoped<Servicios.IDocumentReportService, Servicios.DocumentReportService>();
        services.AddScoped<Servicios.IManifestCostService, Servicios.ManifestCostService>();
        services.AddScoped<Servicios.ISapTransferWriter, Servicios.SapTransferWriter>();
        services.AddScoped<Servicios.ITransportGuideService, Servicios.TransportGuideService>();
        services.AddScoped<Servicios.IManualGuideService, Servicios.ManualGuideService>();
        services.AddScoped<Servicios.IDispatchGroupService, Servicios.DispatchGroupService>();
        services.AddScoped<Servicios.IManifestService, Servicios.ManifestService>();
        services.AddScoped<Servicios.IManifestPdfService, Servicios.ManifestPdfService>();
        services.AddScoped<Servicios.IDispatchGroupPdfService, Servicios.DispatchGroupPdfService>();

        // Reloj inyectable (hora de salida del despacho); los tests lo reemplazan por uno fijo.
        services.TryAddSingleton(TimeProvider.System);

        // Motor de costo (puro): estrategias de cálculo, condiciones de recargo y el motor.
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.FixedPerTripCalculator>();
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.PerM3Calculator>();
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.PerKmCalculator>();
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.PerPackageCalculator>();
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.PerDocumentCalculator>();
        services.AddSingleton<Dominio.Tarifas.IRateCalculator, Dominio.Tarifas.NoChargeCalculator>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.DistinctZonesOverCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.DistinctCommunesOverCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.DepartureTimeFromCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.ShiftIsCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.WeekdayInCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.IsHolidayCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.DocumentsOverCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.PackagesOverCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.VehicleTypeIsCondition>();
        services.AddSingleton<Dominio.Tarifas.ISurchargeCondition, Dominio.Tarifas.ManualQuantityCondition>();
        services.AddSingleton<Dominio.Tarifas.CostEngine>();

        services.AddDbContext<DespachosDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.Despachos requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
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
    }
}
