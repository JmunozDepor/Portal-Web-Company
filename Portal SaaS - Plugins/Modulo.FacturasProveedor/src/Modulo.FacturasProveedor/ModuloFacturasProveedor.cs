using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulo.FacturasProveedor.Data;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor;

/// <summary>
/// Punto de entrada del módulo Facturas de Proveedor (Etapa 1: aprobación y rechazo ante el SII de las facturas
/// recibidas, con reglas por empresa y excepciones por proveedor). Base de datos propia (motor dual, ver
/// Data/FacturasProveedorDbContext.cs); solo referencia PortalSaas.Abstractions. Los documentos recibidos los trae la
/// plataforma (IDteInboxService). Spec: docs/superpowers/specs/2026-10-27-modulo-facturas-proveedor-aprobacion-design.md.
/// </summary>
public sealed class ModuloFacturasProveedor : IModuloPortal
{
    public string ModuleCode => "FacturasProveedor";
    public string Name => "Facturas de Proveedor";

    /// <summary>Base externa propia (instancia + base) -- ver IModuloPortal.RequiresExternalDatabase.</summary>
    public bool RequiresExternalDatabase => true;
    public string Version => "1.0.0";

    /// <summary>Trae wwwroot/css/facturas-proveedor.css (KPI de la bandeja).</summary>
    public bool ServesOwnWwwRoot => true;

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return Group("raiz", null, "Facturas de Proveedor", "bi-receipt-cutoff", 180);
        yield return Leaf("bandeja", "raiz", "Bandeja", "bi-inbox", "/facturas-proveedor/bandeja", 1);
        yield return Leaf("mis-aprobaciones", "raiz", "Mis aprobaciones", "bi-check2-square", "/facturas-proveedor/mis-aprobaciones", 2);

        yield return Group("grupo-config", "raiz", "Configuración", "bi-gear", 3);
        yield return Config("reglas", "Reglas de revisión", "bi-sliders", 1);
        yield return Config("proveedores", "Proveedores con excepciones", "bi-person-exclamation", 2);
        yield return Config("aprobadores", "Aprobadores", "bi-people", 3);
        yield return Config("consultas-sap", "Consultas SAP", "bi-database-gear", 4);
    }

    private static MenuItemDefinition Group(string code, string? parent, string name, string icon, int order) =>
        new() { Code = code, ParentCode = parent, Name = name, Icon = icon, PageRoute = null, Order = order };

    private static MenuItemDefinition Leaf(string code, string parent, string name, string icon, string route, int order) =>
        new() { Code = code, ParentCode = parent, Name = name, Icon = icon, PageRoute = route, Order = order };

    private static MenuItemDefinition Config(string code, string name, string icon, int order) =>
        Leaf(code, "grupo-config", name, icon, "/facturas-proveedor/configuracion/" + code, order);

    public void RegisterServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Catálogo de reglas de revisión: una regla nueva = una clase más registrada acá.
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.OcReferenciadaRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.OcMismoProveedorRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.OcAntiguedadRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.OcSaldoRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.OcAbiertaRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.ProveedorEnSapRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.RevisionManualProveedorRule>();
        services.AddSingleton<Dominio.Reglas.ISupplierInvoiceRule, Dominio.Reglas.Catalogo.PlazoPorVencerRule>();
        services.AddSingleton<Dominio.Reglas.InvoiceRuleEngine>();

        // FacturasProveedorDbContext resuelto self-service vía IExternalDatabaseConnectionService -- motor dual, se
        // decide EN RUNTIME según ModuleExternalConnection.EngineType. CompanyId SIEMPRE obligatorio (sin fallback a
        // nivel Organization). En los jobs, la compañía la fija ICurrentCompanyOverride.
        services.AddDbContext<FacturasProveedorDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.FacturasProveedor requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
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
