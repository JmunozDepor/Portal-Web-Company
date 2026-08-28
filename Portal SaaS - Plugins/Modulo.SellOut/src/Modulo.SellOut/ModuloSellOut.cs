using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.SellOut;

/// <summary>
/// Mantenedores de datos maestros de CLSELLOUT (base SQL Server externa, ajena a
/// HANA/SAP -- reportes OLAP de venta sell-out en clientes retail, alimentados por un
/// pipeline SSIS externo que este plugin NO toca). Portado de PortalSAP_v2, adaptado al
/// contrato actual de PortalSaas.Abstractions -- diferencia relevante frente al
/// original: ISqlServerService (motor único, resolución por EmpresaCodigo con fallback
/// global) fue reemplazado por IExternalDatabaseConnectionService (motor dual
/// Postgres/SqlServer, resolución SIEMPRE por CompanyId, sin fallback a una fila
/// global de la organización -- regla dura del proyecto actual, ver
/// docs/09-GUIA-DESARROLLO-PLUGINS.md §6.1). Aunque CLSELLOUT sigue siendo global a
/// Comercial Depor (sin EMPRESA_CODIGO en sus tablas, ver SellOutDbContext), la
/// RESOLUCIÓN de la connection string ahora exige una Company activa igual -- en la
/// práctica se configura la misma fila de ModuleExternalConnection para cada Company
/// de Comercial Depor que use este módulo.
/// </summary>
public sealed class ModuloSellOut : IModuloPortal
{
    public string ModuleCode => "SellOut";
    public string Name => "Sell Out";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition
        {
            Code = "raiz",
            ParentCode = null,
            Name = "Sell Out",
            // bi-graph-up-arrow: venta y stock hacia el cliente retail (evolución/reporte
            // OLAP de venta sell-out), distinto del bi-truck de Modulo.Wms (logística
            // interna). El "fas fa-chart-line" anterior era Font Awesome -- el sidebar
            // solo carga bootstrap-icons, por eso el ícono no se veía.
            Icon = "bi bi-graph-up-arrow",
            PageRoute = null,
            Order = 110,
        };

        yield return new MenuItemDefinition
        {
            Code = "clientes",
            ParentCode = "raiz",
            Name = "Clientes",
            Icon = "bi bi-people",
            PageRoute = "/sellout/clientes",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "sucursales",
            ParentCode = "raiz",
            Name = "Sucursales",
            Icon = "bi bi-shop",
            PageRoute = "/sellout/sucursales",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "clientesku",
            ParentCode = "raiz",
            Name = "SKU por cliente",
            Icon = "bi bi-upc-scan",
            PageRoute = "/sellout/clientesku",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "departamentos",
            ParentCode = "raiz",
            Name = "Departamentos",
            Icon = "bi bi-diagram-3",
            PageRoute = "/sellout/departamentos",
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "gruposretail",
            ParentCode = "raiz",
            Name = "Grupos retail",
            Icon = "bi bi-collection",
            PageRoute = "/sellout/gruposretail",
            Order = 5,
        };

        yield return new MenuItemDefinition
        {
            Code = "localizaciones",
            ParentCode = "raiz",
            Name = "Localizaciones",
            Icon = "bi bi-pin-map",
            PageRoute = "/sellout/localizaciones",
            Order = 6,
        };

        yield return new MenuItemDefinition
        {
            Code = "geografias",
            ParentCode = "raiz",
            Name = "Geografías",
            Icon = "bi bi-globe-americas",
            PageRoute = "/sellout/geografias",
            Order = 7,
        };

        yield return new MenuItemDefinition
        {
            Code = "layouts",
            ParentCode = "raiz",
            Name = "Configuración de layouts",
            Icon = "bi bi-sliders",
            PageRoute = "/sellout/layouts",
            Order = 8,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Motor dual resuelto EN RUNTIME vía IExternalDatabaseConnectionService, mismo
        // patrón que Modulo.Rendiciones -- CompanyId siempre obligatorio, sin
        // degradar en silencio si la sesión todavía no tiene compañía activa.
        services.AddDbContext<SellOutDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.SellOut requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
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
                    throw new InvalidOperationException($"Motor no soportado: '{connection.EngineType}'.");
            }
        });
    }
}
