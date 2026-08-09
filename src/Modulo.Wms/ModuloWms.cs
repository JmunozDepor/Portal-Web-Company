using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Wms.Data;
using PortalSaas.Abstractions.Contratos;
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
            Code = "mapeo-campos",
            ParentCode = "raiz",
            Name = "Mapeo de Campos",
            PageRoute = "/wms/mapeo-campos",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "configuracion-servicio",
            ParentCode = "raiz",
            Name = "Configuración del Servicio",
            PageRoute = "/wms/configuracion-servicio",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "estado-servicio",
            ParentCode = "raiz",
            Name = "Estado del Servicio",
            PageRoute = "/wms/estado-servicio",
            Order = 3,
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
    }
}
