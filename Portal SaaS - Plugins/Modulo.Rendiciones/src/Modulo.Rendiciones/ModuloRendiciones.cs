using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Servicios;
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
            Icon = "bi-wallet2",
            PageRoute = "/rendiciones/gastos",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "informes",
            ParentCode = "grupo-rendidor",
            Name = "Informes",
            Icon = "bi-journal-text",
            PageRoute = "/rendiciones/informes",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "fondos",
            ParentCode = "grupo-rendidor",
            Name = "Fondos por Rendir",
            Icon = "bi-cash-stack",
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
            Icon = "bi-check2-circle",
            PageRoute = "/rendiciones/aprobaciones",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "grupo-reporte",
            ParentCode = "raiz",
            Name = "Reporte",
            Icon = "bi-bar-chart",
            PageRoute = null,
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "reportes-cierre",
            ParentCode = "grupo-reporte",
            Name = "Cierre y Reportes",
            Icon = "bi-file-earmark-spreadsheet",
            PageRoute = "/rendiciones/reportes/cierre",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "grupo-administrador",
            ParentCode = "raiz",
            Name = "Administrador",
            Icon = "bi-shield-lock",
            PageRoute = null,
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-usuarios-roles",
            ParentCode = "grupo-administrador",
            Name = "Usuarios y Roles",
            Icon = "bi-people",
            PageRoute = "/rendiciones/configuracion/usuarios-roles",
            Order = 0,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-tipos-gasto",
            ParentCode = "grupo-administrador",
            Name = "Tipos de Gasto",
            Icon = "bi-tags",
            PageRoute = "/rendiciones/configuracion/tipos-gasto",
            Order = 1,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-tipos-documento",
            ParentCode = "grupo-administrador",
            Name = "Tipos de Documento",
            Icon = "bi-file-earmark-text",
            PageRoute = "/rendiciones/configuracion/tipos-documento",
            Order = 2,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-grupos",
            ParentCode = "grupo-administrador",
            Name = "Grupos de Aprobación",
            Icon = "bi-diagram-3",
            PageRoute = "/rendiciones/configuracion/grupos",
            Order = 3,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-centros-costo-usuario",
            ParentCode = "grupo-administrador",
            Name = "Centros de Costo por Usuario",
            Icon = "bi-person-lines-fill",
            PageRoute = "/rendiciones/configuracion/centros-costo-usuario",
            Order = 4,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-politicas-gasto",
            ParentCode = "grupo-administrador",
            Name = "Políticas de Gasto",
            Icon = "bi-clipboard-check",
            PageRoute = "/rendiciones/configuracion/politicas-gasto",
            Order = 5,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-proveedores",
            ParentCode = "grupo-administrador",
            Name = "Proveedores de Servicios Externos",
            Icon = "bi-building",
            PageRoute = "/rendiciones/configuracion/proveedores",
            Order = 6,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-consumo-servicios",
            ParentCode = "grupo-administrador",
            Name = "Consumo de Servicios Externos",
            Icon = "bi-graph-up",
            PageRoute = "/rendiciones/configuracion/consumo-servicios",
            Order = 7,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-notificaciones",
            ParentCode = "grupo-administrador",
            Name = "Notificaciones",
            Icon = "bi-bell",
            PageRoute = "/rendiciones/configuracion/notificaciones",
            Order = 8,
        };

        yield return new MenuItemDefinition
        {
            Code = "config-integracion",
            ParentCode = "grupo-administrador",
            Name = "Integración",
            Icon = "bi-plug",
            PageRoute = "/rendiciones/configuracion/integracion",
            Order = 9,
        };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // RendicionesDbContext resuelto self-service vía IExternalDatabaseConnectionService
        // (PortalSaas.Abstractions) -- motor dual, se decide EN RUNTIME cuál proveedor de
        // EF Core usar según lo que la Company tenga configurado
        // (ModuleExternalConnection.EngineType), nunca fijo en el código del plugin.
        // CompanyId SIEMPRE obligatorio -- regla dura del proyecto: todo plugin
        // personaliza su persistencia por Company, sin fallback a nivel Organization (ver
        // IExternalDatabaseConnectionService). Si el usuario todavía no eligió Company en
        // esta sesión (ICurrentCompanyAccessor.HasCompany false), este plugin no puede
        // operar todavía -- se rechaza acá con un mensaje claro en vez de degradar
        // silenciosamente a un alcance más amplio.
        services.AddDbContext<RendicionesDbContext>((sp, options) =>
        {
            var companyAccessor = sp.GetRequiredService<ICurrentCompanyAccessor>();
            var externalDb = sp.GetRequiredService<IExternalDatabaseConnectionService>();

            if (!companyAccessor.HasCompany)
            {
                throw new InvalidOperationException(
                    "Modulo.Rendiciones requiere una compañía activa en la sesión -- seleccioná una compañía antes de continuar.");
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

        services.AddScoped<IExternalServiceProviderService, ExternalServiceProviderService>();
        services.AddScoped<IExternalServiceUsageService, ExternalServiceUsageService>();
        services.AddScoped<IExternalServiceProviderSelector, ExternalServiceProviderSelector>();
        services.AddScoped<IExpenseFundService, ExpenseFundService>();
        services.AddScoped<IExpenseTypeService, ExpenseTypeService>();
        services.AddScoped<IDocumentTypeService, DocumentTypeService>();
        services.AddScoped<IUserCostCenterService, UserCostCenterService>();
        services.AddScoped<IRendicionesUserRoleService, RendicionesUserRoleService>();
        services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
        services.AddSingleton<IReceiptImageProcessor, SkiaReceiptImageProcessor>();
        services.AddScoped<IExpensePolicyService, ExpensePolicyService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        // OCR de comprobantes: dos motores intercambiables (Azure Document Intelligence
        // y Google Gemini), elegidos en runtime por CompositeReceiptExtractorService
        // según lo que la compañía tenga configurado en Configuracion > Proveedores.
        services.AddScoped<AzureDocumentIntelligenceExtractorService>();
        // Guardrail de RPM de Gemini -- estado compartido por proceso, Singleton.
        services.AddSingleton<GeminiRateLimiter>();
        services.AddHttpClient<GeminiReceiptExtractorService>();
        services.AddScoped<IReceiptExtractorService, CompositeReceiptExtractorService>();
        // "Probar" de Configuracion > Proveedores: chequea clave/endpoint contra el
        // endpoint de metadata de cada servicio, sin consumir cuota de OCR. Timeout
        // corto -- es interactivo, no debe colgar la pantalla.
        services.AddHttpClient<IExternalServiceHealthChecker, ExternalServiceHealthChecker>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
        // Typed client -- IRoutingService se resuelve con un HttpClient propio manejado
        // por HttpClientFactory (pooling de sockets), no un "new HttpClient()" a mano.
        services.AddHttpClient<IRoutingService, AzureMapsRoutingService>();
        services.AddScoped<IExpenseApprovalGroupService, ExpenseApprovalGroupService>();
        services.AddScoped<IExpenseReportService, ExpenseReportService>();
        services.AddHostedService<RendicionesReminderBackgroundService>();
        services.AddHostedService<ReceiptRecompressionStartupService>();
        services.AddScoped<IClosingReportService, ClosingReportService>();
        services.AddScoped<ICatalogSyncProvider, SapCatalogSyncProvider>();
    }
}
