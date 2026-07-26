using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones;

/// <summary>
/// Punto de entrada del módulo de Fondos por Rendir y Rendición de Gastos.
///
/// Nombre interno deliberado: "Rendiciones" / "Rendición de Gastos" -- NUNCA usar el
/// nombre de ningún producto comercial existente en el mercado para este dominio, ni
/// en código, ni en comentarios, ni en texto de UI, ni en commits de este repo.
///
/// Este módulo tiene base de datos propia (SQL Server, ver Data/RendicionesDbContext.cs)
/// y solo referencia PortalSaas.Abstractions -- frontera deliberada para poder operar
/// como producto independiente (repo propio, build propio, ver el .csproj). No
/// reutiliza ningún motor de aprobación de otro plugin -- implementa el propio
/// (ExpenseApprovalGroup/ExpenseApprovalGroupLevel/ExpenseApprovalGroupMember).
/// </summary>
public sealed class ModuloRendiciones : IModuloPortal
{
    public string ModuleCode => "Rendiciones";
    public string Name => "Rendición de Gastos";
    public string Version => "1.0.0";

    // Trae wwwroot/css/rendiciones.css propio (badges de estado, tarjetas de lista,
    // tiles KPI que el tema del host no trae) -- cada página lo suma además del
    // shell/tema del portal, nunca lo reemplaza.
    public bool ServesOwnWwwRoot => true;

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition
        {
            Code = "raiz",
            ParentCode = null,
            Name = "Rendiciones",
            Icon = "bi-receipt",
            PageRoute = null,
            Order = 160,
        };

        // Tres subcarpetas: Rendidor/Aprobador/Administrador. El árbol de menú de esta
        // plataforma es autorreferencial a N niveles, así que anidar hojas dos niveles
        // bajo "raiz" (raiz > grupo > hoja) es soportado tal cual.
        yield return new MenuItemDefinition
        {
            Code = "grupo-rendidor",
            ParentCode = "raiz",
            Name = "Rendidor",
            Icon = "bi-person",
            PageRoute = null,
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "gastos",
            ParentCode = "grupo-rendidor",
            Name = "Mis Gastos",
            PageRoute = "/rendiciones/gastos",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "informes",
            ParentCode = "grupo-rendidor",
            Name = "Informes",
            PageRoute = "/rendiciones/informes",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "fondos",
            ParentCode = "grupo-rendidor",
            Name = "Fondos por Rendir",
            PageRoute = "/rendiciones/fondos",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "grupo-aprobador",
            ParentCode = "raiz",
            Name = "Aprobador",
            Icon = "bi-check2-square",
            PageRoute = null,
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "aprobaciones",
            ParentCode = "grupo-aprobador",
            Name = "Aprobaciones",
            PageRoute = "/rendiciones/aprobaciones",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "grupo-administrador",
            ParentCode = "raiz",
            Name = "Administrador",
            Icon = "bi-shield-lock",
            PageRoute = null,
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-tipos-gasto",
            ParentCode = "grupo-administrador",
            Name = "Tipos de Gasto",
            PageRoute = "/rendiciones/configuracion/tipos-gasto",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-tipos-documento",
            ParentCode = "grupo-administrador",
            Name = "Tipos de Documento",
            PageRoute = "/rendiciones/configuracion/tipos-documento",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-grupos",
            ParentCode = "grupo-administrador",
            Name = "Grupos de Aprobación",
            PageRoute = "/rendiciones/configuracion/grupos",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-centros-costo-usuario",
            ParentCode = "grupo-administrador",
            Name = "Centros de Costo por Usuario",
            PageRoute = "/rendiciones/configuracion/centros-costo-usuario",
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-politicas-gasto",
            ParentCode = "grupo-administrador",
            Name = "Políticas de Gasto",
            PageRoute = "/rendiciones/configuracion/politicas-gasto",
            Order = 5,
        };

        yield return new MenuItemDefinition
        {
            Code = "reportes-cierre",
            ParentCode = "grupo-administrador",
            Name = "Cierre y Reportes",
            PageRoute = "/rendiciones/reportes/cierre",
            Order = 6,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-consumo-servicios",
            ParentCode = "grupo-administrador",
            Name = "Consumo de Servicios Externos",
            PageRoute = "/rendiciones/configuracion/consumo-servicios",
            Order = 7,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // TODO (fase de servicios, ver PENDIENTE.md): registrar RendicionesDbContext
        // resolviendo su connection string vía el contrato de conexión externa de la
        // plataforma (equivalente a ISqlServerService.ResolverConnectionStringAsync del
        // original, que hoy NO existe en PortalSaas.Abstractions -- hueco documentado
        // en PENDIENTE.md, prerrequisito antes de que este método compile de verdad
        // contra un DbContext funcional). Ejemplo del patrón esperado (comentado, no
        // ejecutable todavía):
        //
        // services.AddDbContext<RendicionesDbContext>((sp, options) =>
        // {
        //     var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
        //     var externalDb = sp.GetRequiredService<IExternalSqlServerService>(); // TODO: no existe aún
        //     var connectionString = externalDb
        //         .ResolveConnectionStringAsync(ModuleCode, companyAccessor.CompanyId)
        //         .GetAwaiter().GetResult();
        //     options.UseSqlServer(connectionString);
        // });
        //
        // services.AddScoped<IExpenseFundService, ExpenseFundService>();
        // services.AddScoped<IExpenseTypeService, ExpenseTypeService>();
        // services.AddScoped<IDocumentTypeService, DocumentTypeService>();
        // services.AddScoped<IUserCostCenterService, UserCostCenterService>();
        // services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
        // services.AddScoped<IExpensePolicyService, ExpensePolicyService>();
        // services.AddScoped<IExpenseService, ExpenseService>();
        // services.AddScoped<IReceiptExtractorService, AzureDocumentIntelligenceExtractorService>();
        // services.AddHttpClient<IRoutingService, AzureMapsRoutingService>();
        // services.AddScoped<IExpenseApprovalGroupService, ExpenseApprovalGroupService>();
        // services.AddScoped<IExpenseReportService, ExpenseReportService>();
        // services.AddScoped<IClosingReportService, ClosingReportService>();
    }
}
