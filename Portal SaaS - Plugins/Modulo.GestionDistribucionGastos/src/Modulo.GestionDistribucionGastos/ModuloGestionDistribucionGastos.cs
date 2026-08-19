using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.GestionDistribucionGastos.Data;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.GestionDistribucionGastos;

/// <summary>
/// Distribución de centros de costo -- desarrollo A MEDIDA de un cliente puntual
/// (Comercial Depor), NO funcionalidad de plataforma (ver CLAUDE.md del portal --
/// GestionDistribucionGastos/SellOut están explícitamente excluidos de portarse como
/// genérico; este plugin es la excepción confirmada: "cada cliente puede tener sus
/// plugins con particularidades", nunca generalizado a un motor configurable).
///
/// Port DIRECTO de `C:\PROYECTOS\PortalSAP_v2\plugins\Modulo.GestionDistribucionGastos`
/// (a su vez ya un port del proyecto standalone original) -- por mandato explícito del
/// dueño del proyecto, la lógica de negocio (EF Core, motor EERR, stored procedures) NO
/// se modifica ni se generaliza acá, solo se adaptan los puntos de integración con el
/// host nuevo (contratos de PortalSaas.Abstractions en vez de PortalSAP.Abstractions,
/// resolución de conexión vía IExternalDatabaseConnectionService en vez de
/// ISqlServerService, [Authorize] explícito -- ver PageModelBaseGestionGastos.cs).
///
/// La base SQL Server externa (CLDEPORFIN, en sqlsap.cdepor.cl) YA EXISTE con datos
/// reales y un esquema fijo (tablas + 4 stored procedures + 3 vistas, ver Sql/) que no
/// se puede modificar -- por eso este plugin, a diferencia de la regla dura de motor
/// dual del resto de la plataforma, referencia SOLO el proveedor SQL Server (ver el
/// .csproj para el detalle de esa excepción documentada).
///
/// Mismo ModuleCode que el plugin viejo ("GestionGastos") a propósito, por si en algún
/// momento se retoma la sincronización de menú heredada -- acá de todos modos el árbol
/// de menú es nuevo (carpeta raíz propia, ver GetMenu(), la carpeta compartida
/// "Finanzas" del portal viejo no existe acá).
/// </summary>
public sealed class ModuloGestionDistribucionGastos : IModuloPortal
{
    public string ModuleCode => "GestionGastos";
    public string Name => "Distribución de Centros de Costo";
    public string Version => "1.0.0";

    // Trae su propio wwwroot/css/gestiongastos.css (clases .card/.badge/.metric-grid
    // que site.css del host no define) -- renombrado desde "site.css" del original para
    // no colisionar con el site.css real del host (ver la nota de esta sesión: un mismo
    // nombre de archivo en dos wwwroot distintos puede resolver siempre al del host
    // según el orden de UseStaticFiles, dejando el CSS del plugin sin servir en
    // silencio). Cada página lo suma vía @section Styles, nunca reemplaza el tema del
    // portal -- mismo patrón que Modulo.Rendiciones.
    public bool ServesOwnWwwRoot => true;

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition
        {
            Code = "raiz",
            ParentCode = null,
            Name = "Distribución de Gastos",
            // bi-diagram-3-fill (no la versión outline) -- el glifo delgado se veía
            // más chico que cart/bag/box-seam/truck en la misma caja de 46x46px de
            // Home/Inicio (Portal SaaS - Core), la variante rellena empareja el peso
            // visual. Mismo criterio aplicado ahí a Modulo.Administracion/
            // Modulo.ImportacionGenerica.
            Icon = "bi-diagram-3-fill",
            PageRoute = null,
            Order = 170,
        };

        // Una única entrada navegable -- el resto de las pantallas (CuentasPendientes/
        // Reglas/Reporte/Eerr) no tienen nodo de menú propio, se navega entre ellas
        // desde Pages/Shared/_NavInterna.cshtml, tal cual el diseño original (una sola
        // pantalla con nav interna, no una entrada de sidebar por pantalla como
        // Modulo.Ventas/Compras).
        yield return new MenuItemDefinition
        {
            Code = "eerr",
            ParentCode = "raiz",
            Name = "Gestión Gastos EERR",
            PageRoute = "/gestiongastos/panel",
            Order = 1,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // ApplicationDbContext resuelto self-service vía IExternalDatabaseConnectionService
        // (PortalSaas.Abstractions) -- mismo patrón que RendicionesDbContext en
        // Modulo.Rendiciones, adaptado del ISqlServerService.ResolverConnectionStringAsync
        // original. CompanyId SIEMPRE obligatorio -- regla dura del proyecto: todo
        // plugin personaliza su persistencia por Company, sin fallback a nivel
        // Organization (ver IExternalDatabaseConnectionService). Hoy hay una sola fila
        // real (Comercial Depor -> CLDEPORFIN), pero la resolución nunca degrada a un
        // alcance más amplio si esa Company puntual no tiene fila configurada.
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    $"'{ModuleCode}' requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
            }

            var connection = externalDb
                .ResolveConnectionAsync(ModuleCode, companyAccessor.CompanyId)
                .GetAwaiter().GetResult();

            // Motor único a propósito (ver el .csproj) -- el esquema real (SPs T-SQL
            // nativas) nunca va a correr contra Postgres, así que a diferencia de
            // Modulo.Rendiciones acá no hace falta la rama Npgsql.
            if (connection.EngineType != ExternalDatabaseEngineType.SqlServer)
            {
                throw new InvalidOperationException(
                    $"Motor de base de datos externa no soportado para '{ModuleCode}': '{connection.EngineType}' -- este módulo solo soporta SQL Server (esquema fijo, ver Sql/).");
            }

            options.UseSqlServer(connection.ConnectionString);
        });
    }
}
