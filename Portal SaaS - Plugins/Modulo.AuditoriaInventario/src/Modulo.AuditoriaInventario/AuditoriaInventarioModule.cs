using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.AuditoriaInventario;

/// <summary>
/// Punto de entrada del módulo de Auditoría de Inventario (conteo físico por código
/// de barra en sucursales, comparado contra el inventario congelado). ModuleCode
/// confirmado por el dueño del proyecto: "AuditoriaInventario".
///
/// Este módulo es DELIBERADAMENTE autocontenido: no reutiliza
/// Modulo.Inventario.ProductMaster ni Modulo.ImportacionGenerica (evaluado y
/// descartado explícitamente por el dueño del proyecto) -- tiene su propio maestro
/// de producto, su propio importador de congelados y su propia cola de ajustes
/// hacia SAP. Solo referencia PortalSaas.Abstractions, igual que cualquier otro
/// plugin (ver docs/09-GUIA-DESARROLLO-PLUGINS.md §1 del portal).
///
/// El equipo de captura de campo (PWA offline, repo separado) NUNCA toca este
/// código -- consume únicamente Api/V1 (ver Api/V1/*), el único contrato entre los
/// dos repos.
/// </summary>
public sealed class AuditoriaInventarioModule : IModuloPortal
{
    public string ModuleCode => "AuditoriaInventario";
    public string Name => "Auditoría de Inventario";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition
        {
            Code = "raiz",
            ParentCode = null,
            Name = "Auditoría de Inventario",
            Icon = "bi-clipboard-data",
            PageRoute = null,
            Order = 170,
        };

        yield return new MenuItemDefinition
        {
            Code = "sesiones",
            ParentCode = "raiz",
            Name = "Sesiones de Conteo",
            Icon = "bi-upc-scan",
            PageRoute = "/auditoria-inventario/sesiones",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "congelados",
            ParentCode = "raiz",
            Name = "Congelados",
            Icon = "bi-snow",
            PageRoute = "/auditoria-inventario/congelados",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "diferencias",
            ParentCode = "raiz",
            Name = "Diferencias",
            Icon = "bi-bar-chart-line",
            PageRoute = "/auditoria-inventario/diferencias",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "ajustes",
            ParentCode = "raiz",
            Name = "Ajustes",
            Icon = "bi-arrow-left-right",
            PageRoute = "/auditoria-inventario/ajustes",
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "sucursales",
            ParentCode = "raiz",
            Name = "Sucursales",
            Icon = "bi-shop",
            PageRoute = "/auditoria-inventario/sucursales",
            Order = 5,
        };

        yield return new MenuItemDefinition
        {
            Code = "sectores",
            ParentCode = "raiz",
            Name = "Sectores",
            Icon = "bi-grid-3x3-gap",
            PageRoute = "/auditoria-inventario/sectores",
            Order = 6,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // AuditoriaInventarioDbContext resuelto self-service vía
        // IExternalDatabaseConnectionService (PortalSaas.Abstractions) -- motor dual,
        // se decide EN RUNTIME cuál proveedor de EF Core usar según lo que la Company
        // tenga configurado (ModuleExternalConnection.EngineType), nunca fijo en el
        // código del plugin. CompanyId SIEMPRE obligatorio, mismo criterio que
        // Modulo.Rendiciones: sin Company activa en sesión, este módulo se rechaza acá
        // en vez de degradar silenciosamente a un alcance más amplio.
        services.AddDbContext<AuditoriaInventarioDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.AuditoriaInventario requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
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

        // Implementación real de IAuditoriaInventarioApiService (Abstractions),
        // resuelta por PortalSaas.Host para exponer AuditoriaInventarioInboundEndpoints
        // -- ver el comentario en AuditoriaInventarioApiService sobre por qué NO usa
        // el AuditoriaInventarioDbContext de arriba (depende de sesión de portal, la
        // PWA no tiene una).
        services.AddScoped<IAuditoriaInventarioApiService, AuditoriaInventarioApiService>();

        services.AddScoped<IDiferenciaEngine, DiferenciaEngine>();
        services.AddScoped<IAjusteService, AjusteService>();
    }
}
